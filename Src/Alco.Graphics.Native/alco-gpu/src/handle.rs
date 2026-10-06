//! Generational registries keep allocation synchronization separate from object
//! operations. Callers order object removal against access and exclusively own
//! mutable recording state while independent objects can be used concurrently.

use crate::abi::{AlcoHandle, AlcoStatus};
use std::cell::UnsafeCell;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Mutex, OnceLock, RwLock};

/// Generational registry resolving owned snapshots under short shared locks.
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

    /// Copies a projection without cloning the registered object.
    ///
    /// The projection runs under the short registry read lock and must only
    /// inspect wrapper metadata. Core operations, waits, callbacks, and registry
    /// mutation are prohibited; perform them after this method returns.
    pub fn get_copy<R: Copy>(
        &self,
        handle: AlcoHandle,
        project: impl FnOnce(&T) -> R,
    ) -> Result<R, AlcoStatus> {
        if handle.is_null() {
            return Err(AlcoStatus::INVALID_HANDLE);
        }
        let state = self.state.read().unwrap();
        match state.slots.get(handle.index()) {
            Some(Slot::Occupied { generation, value }) if *generation == handle.generation() => {
                Ok(project(value))
            }
            _ => Err(AlcoStatus::INVALID_HANDLE),
        }
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

const SEGMENT_COUNT: usize = 32;
const OCCUPIED: u64 = 1 << 31;
// Geometric segments cover indices 0..u32::MAX, excluding u32::MAX itself.
const STABLE_CAPACITY: usize = u32::MAX as usize;

struct StableSlot<T> {
    state: AtomicU64,
    value: UnsafeCell<Option<T>>,
    // Accessed only while the table's allocation mutex is held.
    free_next: UnsafeCell<Option<usize>>,
}

// Payload access is unsafe and caller-ordered; safe insertion only touches vacant
// slots under the allocation mutex. Segment storage never moves or shrinks.
unsafe impl<T: Send> Sync for StableSlot<T> {}

impl<T> StableSlot<T> {
    fn new() -> Self {
        Self {
            state: AtomicU64::new(1 << 32),
            value: UnsafeCell::new(None),
            free_next: UnsafeCell::new(None),
        }
    }

    fn validate(state: u64, handle: AlcoHandle) -> Result<(), AlcoStatus> {
        if state & OCCUPIED == 0 || (state >> 32) as u32 != handle.generation() {
            Err(AlcoStatus::INVALID_HANDLE)
        } else {
            Ok(())
        }
    }
}

struct StableAllocation {
    len: usize,
    free_head: Option<usize>,
}

/// Stable generational slots with caller-ordered access and no lookup registry lock.
///
/// Segments are published once and never reclaimed until table destruction.
/// Allocation and free-list changes use a short mutex; object access only reads
/// the published generation and occupancy without changing slot state.
pub(crate) struct StableTable<T> {
    segments: [OnceLock<Box<[StableSlot<T>]>>; SEGMENT_COUNT],
    allocation: Mutex<StableAllocation>,
}

impl<T> StableTable<T> {
    /// Creates an empty registry without allocating segments.
    pub const fn new() -> Self {
        Self {
            segments: [const { OnceLock::new() }; SEGMENT_COUNT],
            allocation: Mutex::new(StableAllocation {
                len: 0,
                free_head: None,
            }),
        }
    }

    fn coordinates(index: usize) -> Option<(usize, usize)> {
        if index >= STABLE_CAPACITY {
            return None;
        }
        let position = index + 1;
        let segment = (usize::BITS - 1 - position.leading_zeros()) as usize;
        let base = (1usize << segment) - 1;
        Some((segment, index - base))
    }

    fn slot(&self, handle: AlcoHandle) -> Result<&StableSlot<T>, AlcoStatus> {
        if handle.is_null() || handle.generation() == 0 {
            return Err(AlcoStatus::INVALID_HANDLE);
        }
        let (segment, offset) =
            Self::coordinates(handle.index()).ok_or(AlcoStatus::INVALID_HANDLE)?;
        self.segments[segment]
            .get()
            .and_then(|slots| slots.get(offset))
            .ok_or(AlcoStatus::INVALID_HANDLE)
    }
}

impl<T: Send> StableTable<T> {
    /// Registers an object, reusing a vacant slot without moving live objects.
    pub fn insert(&self, value: T) -> AlcoHandle {
        let mut allocation = self.allocation.lock().unwrap();
        let index = match allocation.free_head {
            Some(index) => index,
            None => {
                assert!(
                    allocation.len < STABLE_CAPACITY,
                    "stable handle table is full"
                );
                allocation.len
            }
        };
        let (segment, offset) = Self::coordinates(index).expect("allocated index must fit");
        let slots = self.segments[segment].get_or_init(|| {
            (0..(1usize << segment))
                .map(|_| StableSlot::new())
                .collect::<Vec<_>>()
                .into_boxed_slice()
        });
        let slot = &slots[offset];
        let state = slot.state.load(Ordering::Acquire);
        debug_assert_eq!(state & OCCUPIED, 0);
        if allocation.free_head.is_some() {
            // The allocation mutex exclusively owns the free-list links.
            allocation.free_head = unsafe { *slot.free_next.get() };
        } else {
            allocation.len += 1;
        }
        // Removal is caller-ordered against access before a slot can be reused.
        unsafe {
            *slot.free_next.get() = None;
            *slot.value.get() = Some(value);
        }
        slot.state.store(state | OCCUPIED, Ordering::Release);
        AlcoHandle((state & !0xFFFF_FFFF) | index as u64)
    }

    /// Borrows an immutable object without cloning it or changing slot state.
    ///
    /// # Safety
    /// The caller must prevent removal or mutable access to this object until
    /// the closure returns, including during callbacks and waits.
    pub unsafe fn with<R>(
        &self,
        handle: AlcoHandle,
        f: impl FnOnce(&T) -> R,
    ) -> Result<R, AlcoStatus>
    where
        T: Sync,
    {
        let slot = self.slot(handle)?;
        StableSlot::<T>::validate(slot.state.load(Ordering::Acquire), handle)?;
        let value = unsafe { &*slot.value.get() }
            .as_ref()
            .expect("occupied slot must contain a value");
        Ok(f(value))
    }

    /// Mutates one caller-exclusive object without changing slot state.
    ///
    /// # Safety
    /// The caller must prevent every other borrow and removal of this object
    /// until the closure returns, including reentrant access.
    pub unsafe fn with_mut<R>(
        &self,
        handle: AlcoHandle,
        f: impl FnOnce(&mut T) -> R,
    ) -> Result<R, AlcoStatus> {
        let slot = self.slot(handle)?;
        StableSlot::<T>::validate(slot.state.load(Ordering::Acquire), handle)?;
        let value = unsafe { &mut *slot.value.get() }
            .as_mut()
            .expect("occupied slot must contain a value");
        Ok(f(value))
    }

    /// Consumes an object and invalidates its generation before reuse.
    ///
    /// Cleanup of the returned value runs outside the allocation mutex.
    ///
    /// # Safety
    /// The caller must order removal after all borrows of this object finish.
    pub unsafe fn remove(&self, handle: AlcoHandle) -> Result<T, AlcoStatus> {
        let mut allocation = self.allocation.lock().unwrap();
        let slot = self.slot(handle)?;
        StableSlot::<T>::validate(slot.state.load(Ordering::Acquire), handle)?;
        let value = unsafe { &mut *slot.value.get() }
            .take()
            .expect("occupied slot must contain a value");
        let generation = handle.generation().wrapping_add(1).max(1);
        unsafe { *slot.free_next.get() = allocation.free_head };
        slot.state
            .store((generation as u64) << 32, Ordering::Release);
        allocation.free_head = Some(handle.index());
        Ok(value)
    }
}

impl<T> Default for StableTable<T> {
    fn default() -> Self {
        Self::new()
    }
}

/// Registry for caller-exclusive mutable pass and bundle recording.
pub(crate) struct RecordingTable<T> {
    entries: StableTable<T>,
}

impl<T> RecordingTable<T> {
    /// Creates an empty recording registry.
    pub const fn new() -> Self {
        Self {
            entries: StableTable::new(),
        }
    }
}

impl<T: Send> RecordingTable<T> {
    /// Registers a mutable object for caller-exclusive access.
    pub fn insert(&self, value: T) -> AlcoHandle {
        self.entries.insert(value)
    }

    /// Mutates one object without holding a registry lock or changing slot state.
    ///
    /// # Safety
    /// The caller must exclusively own access to this object until the closure
    /// returns and must not remove it during that access.
    pub unsafe fn with<R>(
        &self,
        handle: AlcoHandle,
        f: impl FnOnce(&mut T) -> R,
    ) -> Result<R, AlcoStatus> {
        unsafe { self.entries.with_mut(handle, f) }
    }

    /// Consumes a recording object and invalidates its handle.
    ///
    /// # Safety
    /// The caller must order removal after all access to this object finishes.
    pub unsafe fn remove(&self, handle: AlcoHandle) -> Result<T, AlcoStatus> {
        unsafe { self.entries.remove(handle) }
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
    use std::cell::Cell;
    use std::sync::atomic::AtomicUsize;
    use std::sync::{mpsc, Arc};
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
    fn independent_recordings_progress_during_allocation_and_removal() {
        let table = RecordingTable::new();
        let first = table.insert(1);
        let second = table.insert(2);
        let first_state = table
            .entries
            .slot(first)
            .unwrap()
            .state
            .load(Ordering::Acquire);
        let (entered_tx, entered_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel();
        std::thread::scope(|scope| {
            let table_ref = &table;
            scope.spawn(move || unsafe {
                table_ref
                    .with(first, |value| {
                        entered_tx.send(()).unwrap();
                        release_rx.recv().unwrap();
                        *value += 1;
                    })
                    .unwrap()
            });
            entered_rx.recv_timeout(Duration::from_secs(5)).unwrap();
            assert_eq!(
                table
                    .entries
                    .slot(first)
                    .unwrap()
                    .state
                    .load(Ordering::Acquire),
                first_state
            );
            let allocation = table.entries.allocation.try_lock().unwrap();
            drop(allocation);
            unsafe {
                assert_eq!(table.with(second, |value| *value += 1), Ok(()));
                assert_eq!(table.remove(second).unwrap(), 3);
                for value in 0..130 {
                    let handle = table.insert(value);
                    assert_eq!(table.remove(handle).unwrap(), value);
                }
            }
            release_tx.send(()).unwrap();
        });
        unsafe {
            assert_eq!(table.remove(first).unwrap(), 2);
            assert_eq!(
                table.with(first, |_| ()).unwrap_err(),
                AlcoStatus::INVALID_HANDLE
            );
        }
    }

    #[test]
    fn recording_can_be_consumed_after_panic() {
        let table = RecordingTable::new();
        let handle = table.insert(7);
        assert!(
            std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe {
                table.with(handle, |_| panic!("recording failed")).unwrap();
            }))
            .is_err()
        );
        unsafe {
            assert_eq!(table.remove(handle).unwrap(), 7);
            let replacement = table.insert(9);
            assert_eq!(replacement.index(), handle.index());
            assert_ne!(replacement.generation(), handle.generation());
            assert_eq!(
                table.with(handle, |_| ()).unwrap_err(),
                AlcoStatus::INVALID_HANDLE
            );
            assert_eq!(table.remove(handle), Err(AlcoStatus::INVALID_HANDLE));
            assert_eq!(table.remove(replacement).unwrap(), 9);
        }
    }

    #[test]
    fn copy_projection_does_not_clone_registered_object() {
        struct CloneTracked {
            id: u32,
            clones: Arc<AtomicUsize>,
        }
        impl Clone for CloneTracked {
            fn clone(&self) -> Self {
                self.clones.fetch_add(1, Ordering::Relaxed);
                Self {
                    id: self.id,
                    clones: self.clones.clone(),
                }
            }
        }
        let clones = Arc::new(AtomicUsize::new(0));
        let table = HandleTable::new();
        let handle = table.insert(CloneTracked {
            id: 7,
            clones: clones.clone(),
        });
        assert_eq!(table.get_copy(handle, |value| value.id).unwrap(), 7);
        assert_eq!(clones.load(Ordering::Relaxed), 0);
        assert_eq!(table.with(handle, |value| value.id).unwrap(), 7);
        assert_eq!(clones.load(Ordering::Relaxed), 1);
        table.remove(handle).unwrap();
        assert_eq!(
            table.get_copy(handle, |value| value.id),
            Err(AlcoStatus::INVALID_HANDLE)
        );
        assert_eq!(
            table.get_copy(AlcoHandle::NULL, |value| value.id),
            Err(AlcoStatus::INVALID_HANDLE)
        );
    }

    #[test]
    fn stable_shared_access_leaves_slot_state_unchanged() {
        let table = StableTable::new();
        let handle = table.insert(7);
        let unrelated = table.insert(9);
        let state = table.slot(handle).unwrap().state.load(Ordering::Acquire);
        unsafe {
            table
                .with(handle, |value| {
                    assert_eq!(*value, 7);
                    assert_eq!(
                        table.slot(handle).unwrap().state.load(Ordering::Acquire),
                        state
                    );
                    assert_eq!(table.with(handle, |nested| *nested).unwrap(), 7);
                    assert_eq!(table.remove(unrelated).unwrap(), 9);
                    let replacement = table.insert(11);
                    assert_eq!(replacement.index(), unrelated.index());
                    assert_eq!(table.remove(replacement).unwrap(), 11);
                    assert_eq!(*value, 7);
                })
                .unwrap();
            assert_eq!(table.remove(handle).unwrap(), 7);
            assert_eq!(table.remove(handle), Err(AlcoStatus::INVALID_HANDLE));
        }
    }

    #[test]
    fn stable_shared_access_allows_independent_cross_thread_removal() {
        let table = StableTable::new();
        let first = table.insert(1);
        let second = table.insert(2);
        let state = table.slot(first).unwrap().state.load(Ordering::Acquire);
        let (entered_tx, entered_rx) = mpsc::channel();
        let (release_tx, release_rx) = mpsc::channel();
        std::thread::scope(|scope| {
            let table_ref = &table;
            scope.spawn(move || unsafe {
                table_ref
                    .with(first, |value| {
                        entered_tx.send(()).unwrap();
                        release_rx.recv().unwrap();
                        assert_eq!(*value, 1);
                    })
                    .unwrap();
            });
            entered_rx.recv_timeout(Duration::from_secs(5)).unwrap();
            assert_eq!(
                table.slot(first).unwrap().state.load(Ordering::Acquire),
                state
            );
            unsafe {
                assert_eq!(table.with(first, |value| *value), Ok(1));
                assert_eq!(table.remove(second), Ok(2));
                let inserted = table.insert(3);
                assert_eq!(inserted.index(), second.index());
                assert_eq!(table.remove(inserted).unwrap(), 3);
            }
            release_tx.send(()).unwrap();
        });
        assert_eq!(unsafe { table.remove(first) }.unwrap(), 1);
    }

    #[test]
    fn stable_access_can_resume_after_panic() {
        let table = StableTable::new();
        let handle = table.insert(7);
        assert!(
            std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| unsafe {
                table.with(handle, |_| panic!("borrow failed")).unwrap();
            }))
            .is_err()
        );
        unsafe {
            assert_eq!(table.with_mut(handle, |value| *value += 1), Ok(()));
            assert_eq!(table.remove(handle).unwrap(), 8);
        }
    }

    #[test]
    fn stable_growth_keeps_borrowed_slot_address_and_value() {
        let table = StableTable::new();
        let first = table.insert(7);
        let mut inserted = Vec::new();
        unsafe {
            table
                .with(first, |value| {
                    let address = value as *const i32;
                    for index in 1..130 {
                        let handle = table.insert(index);
                        assert_eq!(handle.index(), index as usize);
                        inserted.push(handle);
                    }
                    assert_eq!(
                        table.with(first, |other| other as *const i32).unwrap(),
                        address
                    );
                    assert_eq!(*value, 7);
                })
                .unwrap();
            for (index, handle) in inserted.into_iter().enumerate() {
                assert_eq!(table.remove(handle).unwrap(), index as i32 + 1);
            }
            assert_eq!(table.remove(first).unwrap(), 7);
        }
        for segment in 0..8 {
            assert_eq!(
                table.segments[segment].get().unwrap().len(),
                1usize << segment
            );
        }
        assert!(table.segments[8].get().is_none());
        assert_eq!(StableTable::<i32>::coordinates(u32::MAX as usize), None);
        assert_eq!(
            StableTable::<i32>::coordinates(u32::MAX as usize - 1),
            Some((31, (1usize << 31) - 1))
        );
    }

    #[test]
    fn stable_batch_reuse_preserves_every_free_slot_and_generation() {
        const BATCH: usize = 64;
        let table = StableTable::new();
        let mut handles: Vec<_> = (0..BATCH).map(|i| table.insert(i)).collect();
        for round in 0..128 {
            let stale = handles.clone();
            let stride = (round * 2 + 1) % BATCH;
            let order: Vec<_> = (0..BATCH).map(|i| (i * stride) % BATCH).collect();
            unsafe {
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
            }
            let allocation = table.allocation.lock().unwrap();
            assert_eq!(allocation.len, BATCH);
            assert!(allocation.free_head.is_none());
            drop(allocation);
            for old in stale {
                unsafe {
                    assert_eq!(
                        table.with(old, |_| ()).unwrap_err(),
                        AlcoStatus::INVALID_HANDLE
                    );
                    assert_eq!(
                        table.with_mut(old, |_| ()).unwrap_err(),
                        AlcoStatus::INVALID_HANDLE
                    );
                    assert_eq!(table.remove(old).unwrap_err(), AlcoStatus::INVALID_HANDLE);
                }
            }
        }
    }

    #[test]
    fn stable_partial_reuse_preserves_live_slots_and_free_chain() {
        let table = StableTable::new();
        let handles: Vec<_> = (0..8).map(|i| table.insert(i)).collect();
        unsafe {
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
                assert_eq!(table.with(handles[index], |value| *value).unwrap(), index);
            }
        }
        assert_eq!(table.allocation.lock().unwrap().len, 8);
    }

    #[test]
    fn stable_invalid_and_reused_handles_are_rejected() {
        let table = StableTable::new();
        unsafe {
            for handle in [
                AlcoHandle::NULL,
                AlcoHandle(1),
                AlcoHandle((1u64 << 32) | u32::MAX as u64),
            ] {
                assert_eq!(table.with(handle, |_| ()), Err(AlcoStatus::INVALID_HANDLE));
                assert_eq!(
                    table.with_mut(handle, |_| ()),
                    Err(AlcoStatus::INVALID_HANDLE)
                );
                assert_eq!(table.remove(handle), Err(AlcoStatus::INVALID_HANDLE));
            }
            let original = table.insert(7);
            assert_eq!(table.remove(original).unwrap(), 7);
            let replacement = table.insert(9);
            assert_eq!(replacement.index(), original.index());
            assert_eq!(
                table.with(original, |_| ()),
                Err(AlcoStatus::INVALID_HANDLE)
            );
            assert_eq!(
                table.with_mut(original, |_| ()),
                Err(AlcoStatus::INVALID_HANDLE)
            );
            assert_eq!(table.remove(original), Err(AlcoStatus::INVALID_HANDLE));
            assert_eq!(table.remove(replacement).unwrap(), 9);
        }
    }

    #[test]
    fn stable_concurrent_independent_allocation_and_removal() {
        let table = StableTable::new();
        std::thread::scope(|scope| {
            for worker in 0..8 {
                let table = &table;
                scope.spawn(move || unsafe {
                    for round in 0..256 {
                        let value = worker * 256 + round;
                        let handle = table.insert(value);
                        assert_eq!(table.with(handle, |value| *value).unwrap(), value);
                        assert_eq!(table.with_mut(handle, |value| *value += 1), Ok(()));
                        assert_eq!(table.remove(handle).unwrap(), value + 1);
                        assert_eq!(table.with(handle, |_| ()), Err(AlcoStatus::INVALID_HANDLE));
                    }
                });
            }
        });
        let allocation = table.allocation.lock().unwrap();
        assert!(allocation.len <= 8);
    }

    #[test]
    fn stable_payloads_drop_on_removal_and_table_destruction() {
        struct DropTracked(Arc<AtomicUsize>);
        impl Drop for DropTracked {
            fn drop(&mut self) {
                self.0.fetch_add(1, Ordering::Relaxed);
            }
        }
        let drops = Arc::new(AtomicUsize::new(0));
        let table = StableTable::new();
        let first = table.insert(DropTracked(drops.clone()));
        let second = table.insert(DropTracked(drops.clone()));
        let removed = unsafe { table.remove(first) }.unwrap();
        assert_eq!(drops.load(Ordering::Relaxed), 0);
        drop(removed);
        assert_eq!(drops.load(Ordering::Relaxed), 1);
        let replacement = table.insert(DropTracked(drops.clone()));
        assert_eq!(replacement.index(), first.index());
        unsafe { table.with(second, |_| ()) }.unwrap();
        drop(table);
        assert_eq!(drops.load(Ordering::Relaxed), 3);
    }

    #[test]
    fn recording_supports_send_non_sync_payloads() {
        let table = RecordingTable::new();
        let handle = table.insert(Cell::new(7));
        std::thread::scope(|scope| {
            scope.spawn(|| unsafe { table.with(handle, |value| value.set(9)) }.unwrap());
        });
        assert_eq!(unsafe { table.remove(handle) }.unwrap().get(), 9);
    }
}
