using System;
using NUnit.Framework;

namespace Alco.Test
{
    public class TestBinaryValue
    {
        [Test(Description = "Test BinaryValue.CreateValueEnum and TryGetEnum")]
        public void TestEnumHandling()
        {
            // Test with SerializeMode enum
            SerializeMode originalValue = SerializeMode.Save;

            // Create a BinaryValue from the enum
            BinaryValue binaryValue = BinaryValue.CreateValueEnum(originalValue);

            // Verify it's not null
            Assert.That(binaryValue, Is.Not.Null);
            Assert.That(binaryValue.IsNull, Is.False);

            // Try to get the enum value back
            bool success = binaryValue.TryGetEnum(out SerializeMode retrievedValue);

            // Verify the result
            Assert.That(success, Is.True);
            Assert.That(retrievedValue, Is.EqualTo(originalValue));

            // Test with a different enum value
            SerializeMode anotherValue = SerializeMode.Load;
            BinaryValue anotherBinaryValue = BinaryValue.CreateValueEnum(anotherValue);

            bool anotherSuccess = anotherBinaryValue.TryGetEnum(out SerializeMode anotherRetrievedValue);

            Assert.That(anotherSuccess, Is.True);
            Assert.That(anotherRetrievedValue, Is.EqualTo(anotherValue));
        }

        [Test(Description = "Test BinaryValue.TryGetEnum with invalid data")]
        public void TestEnumHandlingInvalidData()
        {
            // Create a BinaryValue with invalid data (wrong size for enum)
            byte[] invalidData = new byte[] { 1, 2, 3 }; // Too small or large for most enums
            BinaryValue invalidBinaryValue = new BinaryValue(invalidData);

            // Try to get enum from invalid data
            bool success = invalidBinaryValue.TryGetEnum(out SerializeMode value);

            // Verify the result is false due to size mismatch
            Assert.That(success, Is.False);
            Assert.That(value, Is.EqualTo(default(SerializeMode)));

            // Test with empty data
            BinaryValue emptyBinaryValue = new BinaryValue();
            bool emptySuccess = emptyBinaryValue.TryGetEnum(out SerializeMode emptyValue);

            Assert.That(emptySuccess, Is.False);
            Assert.That(emptyValue, Is.EqualTo(default(SerializeMode)));
        }

        [Test(Description = "Test BinaryValue.CreateValueEnum and TryGetEnum with custom enum")]
        public void TestCustomEnumHandling()
        {
            // Define some test values for different enum types

            // Test with byte-based enum
            TestByteEnum byteEnumValue = TestByteEnum.Value2;
            BinaryValue byteEnumBinaryValue = BinaryValue.CreateValueEnum(byteEnumValue);

            bool byteEnumSuccess = byteEnumBinaryValue.TryGetEnum(out TestByteEnum retrievedByteEnum);

            Assert.That(byteEnumSuccess, Is.True);
            Assert.That(retrievedByteEnum, Is.EqualTo(byteEnumValue));

            // Test with int-based enum
            TestIntEnum intEnumValue = TestIntEnum.Value3;
            BinaryValue intEnumBinaryValue = BinaryValue.CreateValueEnum(intEnumValue);

            bool intEnumSuccess = intEnumBinaryValue.TryGetEnum(out TestIntEnum retrievedIntEnum);

            Assert.That(intEnumSuccess, Is.True);
            Assert.That(retrievedIntEnum, Is.EqualTo(intEnumValue));
        }

        [Test(Description = "Test BinaryValue.TryGetEnum with mismatched enum types")]
        public void TestMismatchedEnumTypes()
        {
            // Create a BinaryValue from an int-based enum
            TestIntEnum intEnumValue = TestIntEnum.Value1; // Value 0
            BinaryValue intEnumBinaryValue = BinaryValue.CreateValueEnum(intEnumValue);

            // Try to get a byte-based enum from it
            // This should fail if the sizes are different, but might succeed if the binary representation is compatible
            bool mismatchedSuccess = intEnumBinaryValue.TryGetEnum(out TestByteEnum byteEnumValue);

            // Check if we got the correct value in case of success
            if (mismatchedSuccess)
            {
                // The value should be TestByteEnum.Value1 (0) since that matches the binary representation of TestIntEnum.Value1 (0)
                // This is only predictable if both enums use the same value for the same constant
                Assert.That(byteEnumValue, Is.EqualTo(TestByteEnum.Value1));
            }

            // Create a BinaryValue from a byte-based enum with value 1
            TestByteEnum byteEnum = TestByteEnum.Value2; // Value 1
            BinaryValue byteEnumBinaryValue = BinaryValue.CreateValueEnum(byteEnum);

            // Try to read it as BinaryValueType enum (which is also byte-based)
            bool sameTypeSuccess = byteEnumBinaryValue.TryGetEnum(out BinaryValueType bvtValue);

            // This should succeed since both are byte enums, but values may not match semantically
            Assert.That(sameTypeSuccess, Is.True);
            // BinaryValueType.Array = 0x01, which matches TestByteEnum.Value2 = 1
            Assert.That(bvtValue, Is.EqualTo(BinaryValueType.Array));
        }

        // Test enums with different underlying types
        public enum TestByteEnum : byte
        {
            Value1 = 0,
            Value2 = 1,
            Value3 = 2
        }

        public enum TestIntEnum : int
        {
            Value1 = 0,
            Value2 = 100,
            Value3 = 200
        }
    }
}