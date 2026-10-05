//! Generational handle tables mapping `AlcoHandle` (index + generation) to
//! owned object slots. Destroying an object bumps the slot generation so any
//! stale handle (double-destroy, use-after-destroy) resolves to
//! `AlcoStatus::INVALID_HANDLE` instead of a panic or wrong-object aliasing.
//!
//! Tables are `Mutex`-guarded: the C# side is main-thread driven, but native
//! releases may also run on the GC finalizer thread (see `BaseGPUObject`).
//! `Mutex::new` and `Vec::new` are const, enabling static tables.
//! Lock order everywhere: `slots` before `free_head`.

use crate::abi::{AlcoHandle, AlcoStatus};
use std::sync::Mutex;

pub(crate) struct HandleTable<T> {
    slots: Mutex<Vec<Slot<T>>>,
    /// Free-list head of vacated slot indices (intrusive via `Slot::Vacant.next`).
    free_head: Mutex<Option<usize>>,
}

enum Slot<T> {
    Vacant { generation: u32, next: Option<usize> },
    Occupied { generation: u32, value: T },
}

impl<T> HandleTable<T> {
    /// Const constructor so tables can be used in `static` declarations.
    pub const fn new() -> Self {
        Self {
            slots: Mutex::new(Vec::new()),
            free_head: Mutex::new(None),
        }
    }
}

impl<T> Default for HandleTable<T> {
    fn default() -> Self {
        Self::new()
    }
}

impl<T> HandleTable<T> {
    pub fn insert(&self, value: T) -> AlcoHandle {
        let mut slots = self.slots.lock().unwrap();
        let mut free = self.free_head.lock().unwrap();

        let (index, generation) = match free.take() {
            Some(index) => {
                let generation = match &slots[index] {
                    Slot::Vacant { generation, .. } => *generation,
                    Slot::Occupied { .. } => unreachable!("free-list index must be vacant"),
                };
                slots[index] = Slot::Occupied { generation, value };
                (index, generation)
            }
            None => {
                let generation = 1; // 0 is reserved for the null handle
                slots.push(Slot::Occupied { generation, value });
                (slots.len() - 1, generation)
            }
        };
        AlcoHandle(((generation as u64) << 32) | index as u64)
    }

    /// Runs `f` with mutable access to the object. Fails cleanly on
    /// stale/null handles. The table lock is held for the duration of `f`,
    /// so `f` must not call back into the same table.
    pub fn with<R>(
        &self,
        handle: AlcoHandle,
        f: impl FnOnce(&mut T) -> R,
    ) -> Result<R, AlcoStatus> {
        if handle.is_null() {
            return Err(AlcoStatus::INVALID_HANDLE);
        }
        let mut slots = self.slots.lock().unwrap();
        match slots.get_mut(handle.index()) {
            Some(Slot::Occupied { generation, value, .. }) if *generation == handle.generation() => {
                Ok(f(value))
            }
            _ => Err(AlcoStatus::INVALID_HANDLE),
        }
    }

    /// Takes the value out (destroying the object), bumping the generation.
    /// A second destroy with the same handle returns `INVALID_HANDLE`.
    pub fn remove(&self, handle: AlcoHandle) -> Result<T, AlcoStatus> {
        if handle.is_null() {
            return Err(AlcoStatus::INVALID_HANDLE);
        }
        let mut slots = self.slots.lock().unwrap();
        match slots.get_mut(handle.index()) {
            Some(Slot::Occupied { generation, .. }) if *generation == handle.generation() => {
                let new_generation = generation.wrapping_add(1).max(1);
                let old = std::mem::replace(
                    &mut slots[handle.index()],
                    Slot::Vacant { generation: new_generation, next: None },
                );
                let Slot::Occupied { value, .. } = old else {
                    unreachable!("slot was checked occupied");
                };
                let mut free = self.free_head.lock().unwrap();
                if let Slot::Vacant { next, .. } = &mut slots[handle.index()] {
                    *next = *free;
                }
                *free = Some(handle.index());
                Ok(value)
            }
            _ => Err(AlcoStatus::INVALID_HANDLE),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn insert_with_remove_cycle() {
        let table: HandleTable<u32> = HandleTable::default();
        let h = table.insert(7);
        assert_eq!(table.with(h, |v| *v).unwrap(), 7);
        assert_eq!(table.remove(h).unwrap(), 7);
        assert_eq!(table.remove(h).unwrap_err(), AlcoStatus::INVALID_HANDLE);
    }

    #[test]
    fn generation_bumps_on_reuse() {
        let table: HandleTable<u32> = HandleTable::default();
        let h1 = table.insert(1);
        table.remove(h1).unwrap();
        let h2 = table.insert(2);
        // Same index, different generation — old handle must not resolve.
        assert_eq!(h1.index(), h2.index());
        assert_ne!(h1.generation(), h2.generation());
        assert_eq!(table.remove(h1).unwrap_err(), AlcoStatus::INVALID_HANDLE);
        assert_eq!(table.remove(h2).unwrap(), 2);
    }

    #[test]
    fn null_handle_rejected() {
        let table: HandleTable<u32> = HandleTable::default();
        assert_eq!(table.with(AlcoHandle::NULL, |_| ()).unwrap_err(), AlcoStatus::INVALID_HANDLE);
        assert_eq!(table.remove(AlcoHandle::NULL).unwrap_err(), AlcoStatus::INVALID_HANDLE);
    }
}
