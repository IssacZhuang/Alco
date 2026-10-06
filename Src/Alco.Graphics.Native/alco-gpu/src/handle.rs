//! Typed opaque pointers with caller-ordered borrowing and unique consumption.

use crate::abi::AlcoStatus;
use std::fmt;

/// A pointer to an independently owned native object.
///
/// Copying a handle borrows its identity; it does not create another owner.
/// Non-null pointers must remain valid until every borrow has ended.
#[repr(transparent)]
pub struct Handle<T>(pub *mut T);

impl<T> Copy for Handle<T> {}

impl<T> Clone for Handle<T> {
    fn clone(&self) -> Self {
        *self
    }
}

impl<T> fmt::Debug for Handle<T> {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        fmt::Pointer::fmt(&self.0, f)
    }
}

impl<T> PartialEq for Handle<T> {
    fn eq(&self, other: &Self) -> bool {
        self.0 == other.0
    }
}

impl<T> Eq for Handle<T> {}

// Transfers and shared access still require the documented unsafe borrow contract.
unsafe impl<T: Send> Send for Handle<T> {}
unsafe impl<T: Sync> Sync for Handle<T> {}

impl<T> Handle<T> {
    /// An absent object pointer.
    pub const NULL: Self = Self(std::ptr::null_mut());

    /// Allocates an object and transfers its unique owner to the caller.
    pub fn new(value: T) -> Self {
        Self(Box::into_raw(Box::new(value)))
    }

    /// Returns whether no object pointer was supplied.
    #[inline]
    pub fn is_null(self) -> bool {
        self.0.is_null()
    }

    /// Borrows a live object without acquiring another owner.
    ///
    /// # Safety
    /// A non-null pointer must identify a live object of this type. The owner
    /// must prevent mutation and destruction for the returned borrow's lifetime.
    #[inline]
    pub unsafe fn get<'a>(self) -> Result<&'a T, AlcoStatus> {
        unsafe { self.0.as_ref() }.ok_or_else(|| crate::entry::fail(AlcoStatus::INVALID_HANDLE))
    }

    /// Exclusively borrows a live object.
    ///
    /// # Safety
    /// A non-null pointer must identify a live object of this type. No other
    /// borrow, mutation, callback reentry, or destruction may overlap this borrow.
    #[inline]
    pub unsafe fn get_mut<'a>(self) -> Result<&'a mut T, AlcoStatus> {
        unsafe { self.0.as_mut() }.ok_or_else(|| crate::entry::fail(AlcoStatus::INVALID_HANDLE))
    }

    /// Consumes an object's unique boxed owner.
    ///
    /// # Safety
    /// The pointer must have been returned by `new` and not consumed before.
    /// All borrows must have ended. Arc-backed device/surface owners must use
    /// their own release operation rather than this method.
    #[inline]
    pub unsafe fn take(self) -> Result<Box<T>, AlcoStatus> {
        if self.is_null() {
            Err(crate::entry::fail(AlcoStatus::INVALID_HANDLE))
        } else {
            Ok(unsafe { Box::from_raw(self.0) })
        }
    }
}

impl<T> Default for Handle<T> {
    fn default() -> Self {
        Self::NULL
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::cell::Cell;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::Arc;

    #[test]
    fn handle_has_pointer_layout() {
        assert_eq!(
            std::mem::size_of::<Handle<u32>>(),
            std::mem::size_of::<*mut u32>()
        );
        assert_eq!(
            std::mem::align_of::<Handle<u32>>(),
            std::mem::align_of::<*mut u32>()
        );
    }

    #[test]
    fn null_is_rejected_without_dereferencing() {
        unsafe {
            assert_eq!(Handle::<u32>::NULL.get(), Err(AlcoStatus::INVALID_HANDLE));
            assert_eq!(
                Handle::<u32>::NULL.get_mut(),
                Err(AlcoStatus::INVALID_HANDLE)
            );
            assert_eq!(Handle::<u32>::NULL.take(), Err(AlcoStatus::INVALID_HANDLE));
        }
    }

    #[test]
    fn borrowing_does_not_acquire_shared_ownership() {
        let value = Arc::new(7);
        let handle = Handle::new(value.clone());
        unsafe {
            for _ in 0..32 {
                assert_eq!(**handle.get().unwrap(), 7);
                assert_eq!(Arc::strong_count(&value), 2);
            }
            drop(handle.take().unwrap());
        }
        assert_eq!(Arc::strong_count(&value), 1);
    }

    #[test]
    fn independent_objects_can_be_created_mutated_and_consumed_concurrently() {
        std::thread::scope(|scope| {
            for worker in 0..8 {
                scope.spawn(move || {
                    for round in 0..256 {
                        let handle = Handle::new(Cell::new(worker * 256 + round));
                        unsafe {
                            handle.get_mut().unwrap().set(round);
                            assert_eq!(handle.take().unwrap().get(), round);
                        }
                    }
                });
            }
        });
    }

    #[test]
    fn consumption_drops_payload_exactly_once() {
        struct Tracked(Arc<AtomicUsize>);
        impl Drop for Tracked {
            fn drop(&mut self) {
                self.0.fetch_add(1, Ordering::Relaxed);
            }
        }
        let drops = Arc::new(AtomicUsize::new(0));
        let handle = Handle::new(Tracked(drops.clone()));
        unsafe { drop(handle.take().unwrap()) };
        assert_eq!(drops.load(Ordering::Relaxed), 1);
    }
}
