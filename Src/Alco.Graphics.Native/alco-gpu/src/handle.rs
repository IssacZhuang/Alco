//! Generational registries keep lookup locks separate from object operations.
//! Recording objects reject overlapping mutation instead of blocking unrelated
//! passes. Callers retain resource ownership until all uses have finished.

use crate::abi::{AlcoHandle, AlcoStatus};
use std::cell::UnsafeCell;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, RwLock};

pub(crate) struct HandleTable<T> {
    state: RwLock<TableState<T>>,
}

struct TableState<T> {
    slots: Vec<Slot<T>>,
    free_head: Option<usize>,
}

enum Slot<T> {
    Vacant {
        generation: u32,
        next: Option<usize>,
    },
    Occupied {
        generation: u32,
        value: T,
    },
}

impl<T> HandleTable<T> {
    /// Creates an empty registry.
    pub const fn new() -> Self {
        Self {
            state: RwLock::new(TableState {
                slots: Vec::new(),
                free_head: None,
            }),
        }
    }

    /// Inserts an object, reusing a vacant slot without losing the free list.
    pub fn insert(&self, value: T) -> AlcoHandle {
        let mut state = self.state.write().unwrap();
        let (index, generation) = match state.free_head.take() {
            Some(index) => {
                let (generation, next) = match &state.slots[index] {
                    Slot::Vacant { generation, next } => (*generation, *next),
                    Slot::Occupied { .. } => unreachable!("free-list index must be vacant"),
                };
                state.free_head = next;
                state.slots[index] = Slot::Occupied { generation, value };
                (index, generation)
            }
            None => {
                let generation = 1;
                state.slots.push(Slot::Occupied { generation, value });
                (state.slots.len() - 1, generation)
            }
        };
        AlcoHandle(((generation as u64) << 32) | index as u64)
    }

    /// Removes an object and invalidates its generation before returning it.
    pub fn remove(&self, handle: AlcoHandle) -> Result<T, AlcoStatus> {
        if handle.is_null() {
            return Err(AlcoStatus::INVALID_HANDLE);
        }
        let mut state = self.state.write().unwrap();
        match state.slots.get(handle.index()) {
            Some(Slot::Occupied { generation, .. }) if *generation == handle.generation() => {
                let next_generation = generation.wrapping_add(1).max(1);
                let next = state.free_head;
                let old = std::mem::replace(
                    &mut state.slots[handle.index()],
                    Slot::Vacant {
                        generation: next_generation,
                        next,
                    },
                );
                state.free_head = Some(handle.index());
                let Slot::Occupied { value, .. } = old else {
                    unreachable!("slot was checked occupied");
                };
                Ok(value)
            }
            _ => Err(AlcoStatus::INVALID_HANDLE),
        }
    }
}

impl<T: Clone> HandleTable<T> {
    /// Resolves an owned snapshot under a short shared registry lock.
    pub fn get(&self, handle: AlcoHandle) -> Result<T, AlcoStatus> {
        if handle.is_null() {
            return Err(AlcoStatus::INVALID_HANDLE);
        }
        let state = self.state.read().unwrap();
        match state.slots.get(handle.index()) {
            Some(Slot::Occupied { generation, value }) if *generation == handle.generation() => {
                Ok(value.clone())
            }
            _ => Err(AlcoStatus::INVALID_HANDLE),
        }
    }

    /// Runs an operation after releasing the registry lock.
    pub fn with<R>(&self, handle: AlcoHandle, f: impl FnOnce(&T) -> R) -> Result<R, AlcoStatus> {
        let value = self.get(handle)?;
        Ok(f(&value))
    }
}

impl<T> Default for HandleTable<T> {
    fn default() -> Self {
        Self::new()
    }
}

struct RecordingCell<T> {
    busy: AtomicBool,
    value: UnsafeCell<Option<T>>,
}

// Every access to value holds the nonblocking exclusive claim, and its Arc
// keeps the allocation alive even when a concurrent end removes the handle.
unsafe impl<T: Send> Sync for RecordingCell<T> {}

struct RecordingGuard<'a, T> {
    cell: &'a RecordingCell<T>,
}

impl<T> Drop for RecordingGuard<'_, T> {
    fn drop(&mut self) {
        self.cell.busy.store(false, Ordering::Release);
    }
}

impl<T> RecordingCell<T> {
    fn claim(&self) -> Result<RecordingGuard<'_, T>, AlcoStatus> {
        self.busy
            .compare_exchange(false, true, Ordering::Acquire, Ordering::Relaxed)
            .map_err(|_| AlcoStatus::INVALID_ARGUMENT)?;
        Ok(RecordingGuard { cell: self })
    }
}

/// Registry for caller-exclusive mutable pass and bundle recording.
pub(crate) struct RecordingTable<T> {
    entries: HandleTable<Arc<RecordingCell<T>>>,
}

impl<T> RecordingTable<T> {
    /// Creates an empty recording registry.
    pub const fn new() -> Self {
        Self {
            entries: HandleTable::new(),
        }
    }

    /// Registers a mutable object with independent exclusive access.
    pub fn insert(&self, value: T) -> AlcoHandle {
        self.entries.insert(Arc::new(RecordingCell {
            busy: AtomicBool::new(false),
            value: UnsafeCell::new(Some(value)),
        }))
    }

    /// Mutates one object without holding a registry lock or waiting on other objects.
    pub fn with<R>(
        &self,
        handle: AlcoHandle,
        f: impl FnOnce(&mut T) -> R,
    ) -> Result<R, AlcoStatus> {
        let cell = self.entries.get(handle)?;
        let _guard = cell.claim()?;
        let value = unsafe { &mut *cell.value.get() };
        let value = value.as_mut().ok_or(AlcoStatus::INVALID_HANDLE)?;
        Ok(f(value))
    }

    /// Consumes an idle object; rejected overlapping operations leave its handle intact.
    pub fn remove(&self, handle: AlcoHandle) -> Result<T, AlcoStatus> {
        let cell = self.entries.get(handle)?;
        let _guard = cell.claim()?;
        let value = unsafe { &mut *cell.value.get() };
        let value = value.take().ok_or(AlcoStatus::INVALID_HANDLE)?;
        self.entries.remove(handle)?;
        Ok(value)
    }
}

impl<T> Default for RecordingTable<T> {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::mpsc;
    use std::time::Duration;

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
        let table = HandleTable::new();
        let h1 = table.insert(1);
        table.remove(h1).unwrap();
        let h2 = table.insert(2);
        assert_eq!(h1.index(), h2.index());
        assert_ne!(h1.generation(), h2.generation());
        assert_eq!(table.remove(h1).unwrap_err(), AlcoStatus::INVALID_HANDLE);
        assert_eq!(table.remove(h2).unwrap(), 2);
    }

    #[test]
    fn batch_reuse_preserves_every_free_slot_and_generation() {
        const BATCH: usize = 64;
        let table = HandleTable::new();
        let mut handles: Vec<_> = (0..BATCH).map(|i| table.insert(i)).collect();
        for round in 0..128 {
            let stale = handles.clone();
            let stride = (round * 2 + 1) % BATCH;
            let order: Vec<_> = (0..BATCH).map(|i| (i * stride) % BATCH).collect();
            for &index in &order {
                assert_eq!(table.remove(handles[index]).unwrap(), index);
            }
            for &index in order.iter().rev() {
                let handle = table.insert(index);
                assert_eq!(handle.index(), stale[index].index());
                assert_eq!(handle.generation(), stale[index].generation() + 1);
                assert_eq!(table.with(handle, |v| *v).unwrap(), index);
                handles[index] = handle;
            }
            assert_eq!(table.state.read().unwrap().slots.len(), BATCH);
            assert!(table.state.read().unwrap().free_head.is_none());
            for old in stale {
                assert_eq!(
                    table.with(old, |_| ()).unwrap_err(),
                    AlcoStatus::INVALID_HANDLE
                );
                assert_eq!(table.remove(old).unwrap_err(), AlcoStatus::INVALID_HANDLE);
            }
        }
    }

    #[test]
    fn partial_batch_reuse_keeps_live_slots_and_remaining_free_chain() {
        let table = HandleTable::new();
        let handles: Vec<_> = (0..8).map(|i| table.insert(i)).collect();
        for index in [0, 2, 4, 6] {
            table.remove(handles[index]).unwrap();
        }
        let replacements: Vec<_> = (0..2).map(|i| table.insert(10 + i)).collect();
        assert_eq!(replacements[0].index(), 6);
        assert_eq!(replacements[1].index(), 4);
        table.remove(replacements[0]).unwrap();
        for expected in [6, 2, 0] {
            assert_eq!(table.insert(99).index(), expected);
        }
        for index in [1, 3, 5, 7] {
            assert_eq!(table.with(handles[index], |v| *v).unwrap(), index);
        }
        assert_eq!(table.state.read().unwrap().slots.len(), 8);
    }

    #[test]
    fn null_handle_rejected() {
        let table: HandleTable<u32> = HandleTable::default();
        assert_eq!(
            table.with(AlcoHandle::NULL, |_| ()).unwrap_err(),
            AlcoStatus::INVALID_HANDLE
        );
        assert_eq!(
            table.remove(AlcoHandle::NULL).unwrap_err(),
            AlcoStatus::INVALID_HANDLE
        );
    }

    #[test]
    fn snapshot_operation_does_not_hold_registry_lock() {
        let table = HandleTable::new();
        let handle = table.insert(Arc::new(7));
        table
            .with(handle, |value| {
                assert_eq!(**value, 7);
                assert_eq!(*table.remove(handle).unwrap(), 7);
                table.insert(Arc::new(9));
            })
            .unwrap();
        assert_eq!(table.get(handle).unwrap_err(), AlcoStatus::INVALID_HANDLE);
    }

    #[test]
    fn independent_recordings_progress_and_overlap_is_rejected() {
        let table = RecordingTable::new();
        let first = table.insert(1);
        let second = table.insert(2);
        let (entered_tx, entered_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel();
        std::thread::scope(|scope| {
            let table_ref = &table;
            scope.spawn(move || {
                table_ref
                    .with(first, |value| {
                        entered_tx.send(()).unwrap();
                        release_rx.recv().unwrap();
                        *value += 1;
                    })
                    .unwrap()
            });
            entered_rx.recv_timeout(Duration::from_secs(5)).unwrap();
            let result = table.with(second, |value| *value += 1);
            let overlap = table.with(first, |_| ());
            let consume = table.remove(first);
            release_tx.send(()).unwrap();
            assert_eq!(result, Ok(()));
            assert_eq!(overlap, Err(AlcoStatus::INVALID_ARGUMENT));
            assert_eq!(consume, Err(AlcoStatus::INVALID_ARGUMENT));
        });
        assert_eq!(table.remove(first).unwrap(), 2);
        assert_eq!(table.remove(second).unwrap(), 3);
        assert_eq!(
            table.with(first, |_| ()).unwrap_err(),
            AlcoStatus::INVALID_HANDLE
        );
    }

    #[test]
    fn recording_claim_is_released_after_panic_and_consumption() {
        let table = RecordingTable::new();
        let handle = table.insert(7);
        let stale = table.entries.get(handle).unwrap();
        assert!(std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
            table.with(handle, |_| panic!("recording failed")).unwrap();
        }))
        .is_err());
        assert_eq!(table.remove(handle).unwrap(), 7);
        let _guard = stale.claim().unwrap();
        assert!(unsafe { &*stale.value.get() }.is_none());
        assert_eq!(
            table.with(handle, |_| ()).unwrap_err(),
            AlcoStatus::INVALID_HANDLE
        );
    }
}
