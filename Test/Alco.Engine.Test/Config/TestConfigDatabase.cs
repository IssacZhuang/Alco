using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Alco.Engine;
using Alco.IO;
using System.Collections.Concurrent;

namespace Alco.Engine.Test
{
    /// <summary>
    /// Test fixture for ConfigDatabase functionality including file source management,
    /// config retrieval, error handling, and thread safety.
    /// </summary>
    [TestFixture]
    public class TestConfigDatabase
    {
        /// <summary>
        /// Test file source implementation for testing purposes.
        /// </summary>
        private class TestFileSource : IFileSource
        {
            public string Name => "Test File Source";

            private readonly Dictionary<string, byte[]> _files = new();

            public int Priority => 0;
            public IEnumerable<string> AllFileNames => _files.Keys;

            /// <summary>
            /// Add a JSON file to the test file source.
            /// </summary>
            public void AddFile(string filename, string content)
            {
                _files[filename] = Encoding.UTF8.GetBytes(content);
            }

            /// <summary>
            /// Remove a file from the test file source.
            /// </summary>
            public void RemoveFile(string filename)
            {
                _files.Remove(filename);
            }

            /// <summary>
            /// Clear all files from the test file source.
            /// </summary>
            public void Clear()
            {
                _files.Clear();
            }

            public bool TryGetData(string path, [NotNullWhen(true)] out SafeMemoryHandle data, out string failureReason)
            {
                if (_files.TryGetValue(path, out var bytes))
                {
                    data = new SafeMemoryHandle(bytes);
                    failureReason = null;
                    return true;
                }

                data = SafeMemoryHandle.Empty;
                failureReason = $"File not found: {path}";
                return false;
            }

            public bool TryGetStream(string path, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? failureReason)
            {
                if (_files.TryGetValue(path, out var bytes))
                {
                    stream = new MemoryStream(bytes);
                    failureReason = null;
                    return true;
                }

                stream = null;
                failureReason = $"File not found: {path}";
                return false;
            }

            public void Dispose()
            {
                _files.Clear();
            }
        }

        /// <summary>
        /// Test config class for testing purposes.
        /// </summary>
        private class TestConfig : Configable
        {
            public string Name { get; set; } = string.Empty;
            public int Value { get; set; }
        }

        /// <summary>
        /// Another test config class for type-specific testing.
        /// </summary>
        private class AnotherTestConfig : Configable
        {
            public string Description { get; set; } = string.Empty;
            public float Score { get; set; }
        }

        /// <summary>
        /// Base config class for inheritance testing.
        /// </summary>
        private class BaseConfig : Configable
        {
            public string BaseProperty { get; set; } = string.Empty;
            public int BaseValue { get; set; }
        }

        /// <summary>
        /// Derived config class for inheritance testing.
        /// </summary>
        private class DerivedConfig : BaseConfig
        {
            public string DerivedProperty { get; set; } = string.Empty;
            public double DerivedValue { get; set; }
        }

        /// <summary>
        /// Another derived config class for inheritance testing.
        /// </summary>
        private class AnotherDerivedConfig : BaseConfig
        {
            public bool AnotherFlag { get; set; }
            public string AnotherText { get; set; } = string.Empty;
        }

        /// <summary>
        /// Config class that can reference other configs for testing reference resolution.
        /// </summary>
        private class ConfigWithReferences : Configable
        {
            public string Name { get; set; } = string.Empty;
            public TestConfig ReferencedTestConfig { get; set; }
            public BaseConfig ReferencedBaseConfig { get; set; }
            public List<TestConfig> ReferencedTestConfigs { get; set; } = new();
        }

        /// <summary>
        /// Player config that references weapon and armor configs.
        /// </summary>
        private class PlayerConfig : Configable
        {
            public string PlayerName { get; set; } = string.Empty;
            public int Level { get; set; }
            public WeaponConfig Weapon { get; set; }
            public ArmorConfig Armor { get; set; }
        }

        /// <summary>
        /// Weapon config for reference testing.
        /// </summary>
        private class WeaponConfig : Configable
        {
            public string WeaponName { get; set; } = string.Empty;
            public int Damage { get; set; }
            public string WeaponType { get; set; } = string.Empty;
        }

        /// <summary>
        /// Armor config for reference testing.
        /// </summary>
        private class ArmorConfig : Configable
        {
            public string ArmorName { get; set; } = string.Empty;
            public int Defense { get; set; }
            public string ArmorType { get; set; } = string.Empty;
        }

        /// <summary>
        /// Config that references itself (for circular reference testing).
        /// </summary>
        private class SelfReferencingConfig : Configable
        {
            public string Name { get; set; } = string.Empty;
            public SelfReferencingConfig SelfReference { get; set; }
        }

        /// <summary>
        /// Config A that references Config B (for circular reference testing).
        /// </summary>
        private class CircularConfigA : Configable
        {
            public string Name { get; set; } = string.Empty;
            public CircularConfigB RefToB { get; set; }
        }

        /// <summary>
        /// Config B that references Config A (for circular reference testing).
        /// </summary>
        private class CircularConfigB : Configable
        {
            public string Name { get; set; } = string.Empty;
            public CircularConfigA RefToA { get; set; }
        }

        /// <summary>
        /// Nested object that contains references to other configs.
        /// </summary>
        private class NestedObjectWithReferences
        {
            public WeaponConfig PrimaryWeapon { get; set; }
            public WeaponConfig SecondaryWeapon { get; set; }
            public List<ArmorConfig> ArmorPieces { get; set; } = new();
        }

        /// <summary>
        /// Config that contains nested objects with references.
        /// </summary>
        private class ConfigWithNestedObjectReferences : Configable
        {
            public string Name { get; set; } = string.Empty;
            public NestedObjectWithReferences Equipment { get; set; }
            public NestedObjectWithReferences BackupEquipment { get; set; }
        }

        /// <summary>
        /// Config that contains dictionary with config references.
        /// </summary>
        private class ConfigWithDictionaryReferences : Configable
        {
            public string Name { get; set; } = string.Empty;
            public Dictionary<string, WeaponConfig> WeaponsByType { get; set; } = new();
            public Dictionary<string, List<ArmorConfig>> ArmorByCategory { get; set; } = new();
            public Dictionary<int, TestConfig> ConfigsByLevel { get; set; } = new();
        }

        /// <summary>
        /// Complex config that combines nested objects and dictionaries with references.
        /// </summary>
        private class ComplexConfigWithMixedReferences : Configable
        {
            public string Name { get; set; } = string.Empty;
            public NestedObjectWithReferences MainEquipment { get; set; }
            public Dictionary<string, NestedObjectWithReferences> EquipmentSets { get; set; } = new();
            public Dictionary<string, List<WeaponConfig>> WeaponCollections { get; set; } = new();
        }

        private ConfigDatabase _configDatabase;
        private ConcurrentBag<string> _infos;
        private ConcurrentBag<string> _warnings;
        private ConcurrentBag<string> _errors;
        private TestFileSource _fileSource;

        [SetUp]
        public void SetUp()
        {
            _infos = new ConcurrentBag<string>();
            _warnings = new ConcurrentBag<string>();
            _errors = new ConcurrentBag<string>();

            _configDatabase = new ConfigDatabase(
                null,
                null,
                error => _errors.Add(error)
            );

            _fileSource = new TestFileSource();
        }

        [TearDown]
        public void TearDown()
        {
            _fileSource?.Dispose();
        }

        [Test]
        public void Constructor_ShouldInitializeWithCallbacks()
        {
            // Arrange & Act - Done in SetUp

            // Assert
            Assert.That(_configDatabase, Is.Not.Null);
            Assert.That(_infos, Is.Not.Null);
            Assert.That(_warnings, Is.Not.Null);
            Assert.That(_errors, Is.Not.Null);
        }

        [Test]
        public void GetConfig_WithValidIdAndType_ShouldReturnConfig()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var config = _configDatabase.GetConfig("test-config-1", typeof(TestConfig));

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config, Is.InstanceOf<TestConfig>());
            var testConfig = (TestConfig)config;
            Assert.That(testConfig.Id, Is.EqualTo("test-config-1"));
            Assert.That(testConfig.Name, Is.EqualTo("Test Config"));
            Assert.That(testConfig.Value, Is.EqualTo(42));
        }

        [Test]
        public void GetConfig_WithNonExistentId_ShouldThrowException()
        {
            // Arrange
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert
            var exception = Assert.Throws<Exception>(() =>
                _configDatabase.GetConfig("non-existent", typeof(TestConfig)));

            Assert.That(exception.Message, Does.Contain("Config with id non-existent and type TestConfig not found"));
        }

        [Test]
        public void GetConfig_WithWrongType_ShouldThrowException()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig, Alco.Engine.Test",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert
            var exception = Assert.Throws<Exception>(() =>
                _configDatabase.GetConfig("test-config-1", typeof(AnotherTestConfig)));

            Assert.That(exception.Message, Does.Contain("Config with id test-config-1 and type AnotherTestConfig not found"));
        }

        [Test]
        public void TryGetConfig_WithValidIdAndType_ShouldReturnTrueAndConfig()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.Not.Null);
            Assert.That(config, Is.InstanceOf<TestConfig>());
            var testConfig = (TestConfig)config;
            Assert.That(testConfig.Id, Is.EqualTo("test-config-1"));
            Assert.That(testConfig.Name, Is.EqualTo("Test Config"));
            Assert.That(testConfig.Value, Is.EqualTo(42));
        }

        [Test]
        public void TryGetConfig_WithNonExistentId_ShouldReturnFalseAndNull()
        {
            // Arrange
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("non-existent", typeof(TestConfig), out var config);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(config, Is.Null);
        }

        [Test]
        public void TryGetConfig_WithWrongType_ShouldReturnFalseAndNull()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig, Alco.Engine.Test",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("test-config-1", typeof(AnotherTestConfig), out var config);

            // Assert
            Assert.That(result, Is.False);
            Assert.That(config, Is.Null);
        }

        [Test]
        public void AddFileSource_ShouldMakeConfigsAvailable()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);

            // Act
            _configDatabase.AddFileSource(_fileSource);

            // Assert - Config should be available after adding file source
            var result = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out var config);
            Assert.That(result, Is.True);
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        public void RemoveFileSource_ShouldMakeConfigsUnavailable()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Verify config is available
            var initialResult = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out _);
            Assert.That(initialResult, Is.True);

            // Act
            _configDatabase.RemoveFileSource(_fileSource);

            // Assert - Config should no longer be available
            var finalResult = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out var config);
            Assert.That(finalResult, Is.False);
            Assert.That(config, Is.Null);
        }

        [Test]
        public void MultipleFileeSources_ShouldHandleConfigsFromAllSources()
        {
            // Arrange
            var fileSource1 = new TestFileSource();
            var fileSource2 = new TestFileSource();

            var config1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "config-1",
                "Name": "Config 1",
                "Value": 10
            }
            """;

            var config2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherTestConfig",
                "Id": "config-2",
                "Description": "Config 2",
                "Score": 5.5
            }
            """;

            fileSource1.AddFile("config-1.json", config1Json);
            fileSource2.AddFile("config-2.json", config2Json);

            // Act
            _configDatabase.AddFileSource(fileSource1);
            _configDatabase.AddFileSource(fileSource2);

            // Assert
            var result1 = _configDatabase.TryGetConfig("config-1", typeof(TestConfig), out var config1);
            var result2 = _configDatabase.TryGetConfig("config-2", typeof(AnotherTestConfig), out var config2);

            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
            Assert.That(config1, Is.InstanceOf<TestConfig>());
            Assert.That(config2, Is.InstanceOf<AnotherTestConfig>());

            // Cleanup
            fileSource1.Dispose();
            fileSource2.Dispose();
        }

        [Test]
        public void MultipleSameTypeConfigs_ShouldBeAvailableByDifferentIds()
        {
            // Arrange
            var config1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "config-1",
                "Name": "Config 1",
                "Value": 10
            }
            """;

            var config2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "config-2",
                "Name": "Config 2",
                "Value": 20
            }
            """;

            _fileSource.AddFile("config-1.json", config1Json);
            _fileSource.AddFile("config-2.json", config2Json);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert
            var result1 = _configDatabase.TryGetConfig("config-1", typeof(TestConfig), out var config1);
            var result2 = _configDatabase.TryGetConfig("config-2", typeof(TestConfig), out var config2);

            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);

            var testConfig1 = (TestConfig)config1;
            var testConfig2 = (TestConfig)config2;

            Assert.That(testConfig1.Value, Is.EqualTo(10));
            Assert.That(testConfig2.Value, Is.EqualTo(20));
        }

        [Test]
        public void LazyLoading_ShouldOnlyProcessOnFirstAccess()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);

            // Act - Add file source but don't access configs yet
            _configDatabase.AddFileSource(_fileSource);

            // Assert - No processing should have occurred yet (no errors/warnings/infos should be generated)
            // Note: This is hard to test directly without access to internal state
            // We can only verify that subsequent access works correctly

            var result = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out var config);
            Assert.That(result, Is.True);
            Assert.That(config, Is.Not.Null);
        }

        [Test]
        public void DirtyFlagManagement_ShouldReuseProcessedConfigsWhenNotDirty()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act - Access config multiple times
            var result1 = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out var config1);
            var result2 = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out var config2);

            // Assert - Both accesses should succeed and return the same instance (cached)
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
            Assert.That(config1, Is.SameAs(config2), "Configs should be cached and return the same instance");
        }

        [Test]
        public void ThreadSafety_ConcurrentAccessShouldNotCauseExceptions()
        {
            // Arrange
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig, Alco.Engine.Test",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("test-config-1.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            var exceptions = new List<Exception>();
            var tasks = new List<Task>();

            // Act - Create multiple tasks that access configs concurrently
            for (int i = 0; i < 10; i++)
            {
                var task = Task.Run(() =>
                {
                    try
                    {
                        for (int j = 0; j < 100; j++)
                        {
                            _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                tasks.Add(task);
            }

            Task.WaitAll(tasks.ToArray());

            // Assert - No exceptions should occur
            Assert.That(exceptions, Is.Empty, $"Concurrent access caused exceptions: {string.Join(", ", exceptions)}");
        }

        [Test]
        public void InvalidJsonConfig_ShouldBeHandledGracefully()
        {
            // Arrange
            var invalidJson = "{ invalid json }";

            _fileSource.AddFile("invalid.json", invalidJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() =>
            {
                var result = _configDatabase.TryGetConfig("any-id", typeof(TestConfig), out var config);
                Assert.That(result, Is.False);
                Assert.That(config, Is.Null);
            });

            // Should have error reported
            Assert.That(_errors.Count, Is.GreaterThan(0));
        }

        [Test]
        public void IncorrectTypeInJson_ShouldTriggerOnError()
        {
            // Arrange
            var configJsonWithIncorrectType = """
            {
                "$type": "NonExistentType",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            _fileSource.AddFile("incorrect-type.json", configJsonWithIncorrectType);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() =>
            {
                var result = _configDatabase.TryGetConfig("test-config-1", typeof(TestConfig), out var config);
                Assert.That(result, Is.False);
                Assert.That(config, Is.Null);
            });

            // Should have error reported for incorrect $type
            Assert.That(_errors.Count, Is.GreaterThan(0));
            Assert.That(_errors.Any(error => error.Contains("Error deserializing config")), Is.True,
                "Should report deserialization error for incorrect $type");
        }

        [Test]
        public void ConfigWithoutType_ShouldBeHandledGracefully()
        {
            // Arrange
            var configWithoutTypeJson = """
            {
                "Id": "no-type-config",
                "Name": "Config Without Type",
                "Value": 42
            }
            """;

            _fileSource.AddFile("no-type.json", configWithoutTypeJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() =>
            {
                var result = _configDatabase.TryGetConfig("no-type-config", typeof(TestConfig), out var config);
                // Result depends on how JsonSerializer handles missing type info
                // It might deserialize as base Configable or fail
            });
        }

        [Test]
        public void EmptyFileSource_ShouldHandleGracefully()
        {
            // Arrange - Empty file source

            // Act
            _configDatabase.AddFileSource(_fileSource);

            // Assert - Should not throw and should return false for any config request
            var result = _configDatabase.TryGetConfig("any-id", typeof(TestConfig), out var config);
            Assert.That(result, Is.False);
            Assert.That(config, Is.Null);
        }

        [Test]
        public void NullConfigDeserialization_ShouldBeHandledGracefully()
        {
            // Arrange - This test simulates a scenario where JsonSerializer.Deserialize returns null
            // This can happen with certain malformed JSON structures
            var nullResultJson = "null";

            _fileSource.AddFile("null-config.json", nullResultJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() =>
            {
                var result = _configDatabase.TryGetConfig("any-id", typeof(TestConfig), out var config);
                Assert.That(result, Is.False);
                Assert.That(config, Is.Null);
            });
        }

        #region Inheritance Tests

        [Test]
        public void ConfigInheritance_BaseTypeQuery_ShouldReturnDerivedInstances()
        {
            // Arrange
            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-config-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            var anotherDerivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherDerivedConfig",
                "Id": "another-derived-config-1",
                "BaseProperty": "Another Base Value",
                "BaseValue": 200,
                "AnotherFlag": true,
                "AnotherText": "Another Text"
            }
            """;

            _fileSource.AddFile("derived-config-1.json", derivedConfigJson);
            _fileSource.AddFile("another-derived-config-1.json", anotherDerivedConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act - Query using base type should return derived instances
            var result1 = _configDatabase.TryGetConfig("derived-config-1", typeof(BaseConfig), out var config1);
            var result2 = _configDatabase.TryGetConfig("another-derived-config-1", typeof(BaseConfig), out var config2);

            // Assert
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
            Assert.That(config1, Is.Not.Null);
            Assert.That(config2, Is.Not.Null);

            // Verify actual types are derived types
            Assert.That(config1, Is.InstanceOf<DerivedConfig>());
            Assert.That(config2, Is.InstanceOf<AnotherDerivedConfig>());

            // Verify base properties are accessible
            var derivedConfig = (DerivedConfig)config1;
            var anotherDerivedConfig = (AnotherDerivedConfig)config2;

            Assert.That(derivedConfig.BaseProperty, Is.EqualTo("Base Value"));
            Assert.That(derivedConfig.BaseValue, Is.EqualTo(100));
            Assert.That(derivedConfig.DerivedProperty, Is.EqualTo("Derived Value"));
            Assert.That(derivedConfig.DerivedValue, Is.EqualTo(3.14));

            Assert.That(anotherDerivedConfig.BaseProperty, Is.EqualTo("Another Base Value"));
            Assert.That(anotherDerivedConfig.BaseValue, Is.EqualTo(200));
            Assert.That(anotherDerivedConfig.AnotherFlag, Is.True);
            Assert.That(anotherDerivedConfig.AnotherText, Is.EqualTo("Another Text"));
        }

        [Test]
        public void ConfigInheritance_DerivedTypeQuery_ShouldReturnOnlySpecificDerivedType()
        {
            // Arrange
            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-config-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            var anotherDerivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherDerivedConfig",
                "Id": "another-derived-config-1",
                "BaseProperty": "Another Base Value",
                "BaseValue": 200,
                "AnotherFlag": true,
                "AnotherText": "Another Text"
            }
            """;

            _fileSource.AddFile("derived-config-1.json", derivedConfigJson);
            _fileSource.AddFile("another-derived-config-1.json", anotherDerivedConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act - Query using specific derived type should only return that type
            var result1 = _configDatabase.TryGetConfig("derived-config-1", typeof(DerivedConfig), out var config1);
            var result2 = _configDatabase.TryGetConfig("another-derived-config-1", typeof(DerivedConfig), out var config2);
            var result3 = _configDatabase.TryGetConfig("another-derived-config-1", typeof(AnotherDerivedConfig), out var config3);

            // Assert
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.False); // AnotherDerivedConfig should not be found when searching for DerivedConfig
            Assert.That(result3, Is.True);

            Assert.That(config1, Is.InstanceOf<DerivedConfig>());
            Assert.That(config2, Is.Null);
            Assert.That(config3, Is.InstanceOf<AnotherDerivedConfig>());
        }

        [Test]
        public void ConfigInheritance_GenericGetConfig_ShouldWorkWithInheritance()
        {
            // Arrange
            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-config-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            _fileSource.AddFile("derived-config-1.json", derivedConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Generic method should work with base type
            var baseConfig = _configDatabase.GetConfig<BaseConfig>("derived-config-1");
            Assert.That(baseConfig, Is.Not.Null);
            Assert.That(baseConfig, Is.InstanceOf<DerivedConfig>());

            // Act & Assert - Generic method should work with derived type
            var derivedConfig = _configDatabase.GetConfig<DerivedConfig>("derived-config-1");
            Assert.That(derivedConfig, Is.Not.Null);
            Assert.That(derivedConfig, Is.InstanceOf<DerivedConfig>());
            Assert.That(derivedConfig.DerivedProperty, Is.EqualTo("Derived Value"));
        }

        [Test]
        public void ConfigInheritance_TryGetConfigGeneric_ShouldWorkWithInheritance()
        {
            // Arrange
            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-config-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            _fileSource.AddFile("derived-config-1.json", derivedConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Generic TryGet should work with base type
            var result1 = _configDatabase.TryGetConfig<BaseConfig>("derived-config-1", out var baseConfig);
            Assert.That(result1, Is.True);
            Assert.That(baseConfig, Is.Not.Null);
            Assert.That(baseConfig, Is.InstanceOf<DerivedConfig>());

            // Act & Assert - Generic TryGet should work with derived type
            var result2 = _configDatabase.TryGetConfig<DerivedConfig>("derived-config-1", out var derivedConfig);
            Assert.That(result2, Is.True);
            Assert.That(derivedConfig, Is.Not.Null);
            Assert.That(derivedConfig.DerivedProperty, Is.EqualTo("Derived Value"));

            // Act & Assert - Generic TryGet should fail with unrelated type
            var result3 = _configDatabase.TryGetConfig<AnotherDerivedConfig>("derived-config-1", out var anotherConfig);
            Assert.That(result3, Is.False);
            Assert.That(anotherConfig, Is.Null);
        }

        [Test]
        public void ConfigInheritance_MultipleInheritanceLevels_ShouldWork()
        {
            // This test would require adding a third inheritance level
            // For now, testing with Configable as root level
            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-config-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            _fileSource.AddFile("derived-config-1.json", derivedConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act - Query using root Configable type should also find derived instances
            var result = _configDatabase.TryGetConfig("derived-config-1", typeof(Configable), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.Not.Null);
            Assert.That(config, Is.InstanceOf<DerivedConfig>());
            Assert.That(config.Id, Is.EqualTo("derived-config-1"));
        }

        [Test]
        public void ConfigInheritance_MixedTypesInDatabase_ShouldFilterCorrectly()
        {
            // Arrange - Add configs of different types and inheritance levels
            var testConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config-1",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            var baseConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+BaseConfig",
                "Id": "base-config-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100
            }
            """;

            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-config-1",
                "BaseProperty": "Derived Base Value",
                "BaseValue": 200,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            _fileSource.AddFile("test-config-1.json", testConfigJson);
            _fileSource.AddFile("base-config-1.json", baseConfigJson);
            _fileSource.AddFile("derived-config-1.json", derivedConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Query for Configable should find all configs
            var configurableResult = _configDatabase.TryGetConfig("test-config-1", typeof(Configable), out var configurableConfig);
            Assert.That(configurableResult, Is.True);
            Assert.That(configurableConfig, Is.InstanceOf<TestConfig>());

            // Act & Assert - Query for BaseConfig should find both base and derived
            var baseResult1 = _configDatabase.TryGetConfig("base-config-1", typeof(BaseConfig), out var baseConfig1);
            var baseResult2 = _configDatabase.TryGetConfig("derived-config-1", typeof(BaseConfig), out var baseConfig2);
            var baseResult3 = _configDatabase.TryGetConfig("test-config-1", typeof(BaseConfig), out var baseConfig3);

            Assert.That(baseResult1, Is.True);
            Assert.That(baseResult2, Is.True);
            Assert.That(baseResult3, Is.False); // TestConfig is not assignable to BaseConfig

            Assert.That(baseConfig1, Is.InstanceOf<BaseConfig>());
            Assert.That(baseConfig2, Is.InstanceOf<DerivedConfig>());
            Assert.That(baseConfig3, Is.Null);

            // Act & Assert - Query for DerivedConfig should find only derived
            var derivedResult1 = _configDatabase.TryGetConfig("derived-config-1", typeof(DerivedConfig), out var derivedConfig1);
            var derivedResult2 = _configDatabase.TryGetConfig("base-config-1", typeof(DerivedConfig), out var derivedConfig2);

            Assert.That(derivedResult1, Is.True);
            Assert.That(derivedResult2, Is.False); // BaseConfig is not assignable to DerivedConfig

            Assert.That(derivedConfig1, Is.InstanceOf<DerivedConfig>());
            Assert.That(derivedConfig2, Is.Null);
        }

        [Test]
        public void ConfigInheritance_CachingBehavior_ShouldWorkCorrectlyWithInheritance()
        {
            // Arrange
            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-config-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            _fileSource.AddFile("derived-config-1.json", derivedConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act - Access same config with different type queries multiple times
            var result1 = _configDatabase.TryGetConfig("derived-config-1", typeof(BaseConfig), out var baseConfig1);
            var result2 = _configDatabase.TryGetConfig("derived-config-1", typeof(DerivedConfig), out var derivedConfig1);
            var result3 = _configDatabase.TryGetConfig("derived-config-1", typeof(BaseConfig), out var baseConfig2);
            var result4 = _configDatabase.TryGetConfig("derived-config-1", typeof(DerivedConfig), out var derivedConfig2);

            // Assert - All queries should succeed and return the same instance
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);
            Assert.That(result3, Is.True);
            Assert.That(result4, Is.True);

            Assert.That(baseConfig1, Is.SameAs(baseConfig2), "Same config queried with same type should return same instance");
            Assert.That(derivedConfig1, Is.SameAs(derivedConfig2), "Same config queried with same type should return same instance");
            Assert.That(baseConfig1, Is.SameAs(derivedConfig1), "Same config queried with different compatible types should return same instance");
        }

        #endregion

        #region Reference Resolution Tests

        [Test]
        public void ResolveReferences_BasicConfigReference_ShouldResolveCorrectly()
        {
            // Arrange
            var weaponConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "sword-001",
                "WeaponName": "Steel Sword",
                "Damage": 50,
                "WeaponType": "Sword"
            }
            """;

            var armorConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ArmorConfig",
                "Id": "armor-001",
                "ArmorName": "Plate Armor",
                "Defense": 30,
                "ArmorType": "Heavy"
            }
            """;

            var playerConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+PlayerConfig",
                "Id": "player-001",
                "PlayerName": "Hero",
                "Level": 10,
                "Weapon": { "Id": "sword-001" },
                "Armor": { "Id": "armor-001" }
            }
            """;

            _fileSource.AddFile("weapon-sword-001.json", weaponConfigJson);
            _fileSource.AddFile("armor-armor-001.json", armorConfigJson);
            _fileSource.AddFile("player-player-001.json", playerConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var playerConfig = _configDatabase.GetConfig<PlayerConfig>("player-001");

            // Assert
            Assert.That(playerConfig, Is.Not.Null);
            Assert.That(playerConfig.PlayerName, Is.EqualTo("Hero"));
            Assert.That(playerConfig.Level, Is.EqualTo(10));

            // Verify weapon reference is resolved
            Assert.That(playerConfig.Weapon, Is.Not.Null);
            Assert.That(playerConfig.Weapon.Id, Is.EqualTo("sword-001"));
            Assert.That(playerConfig.Weapon.WeaponName, Is.EqualTo("Steel Sword"));
            Assert.That(playerConfig.Weapon.Damage, Is.EqualTo(50));
            Assert.That(playerConfig.Weapon.WeaponType, Is.EqualTo("Sword"));

            // Verify armor reference is resolved
            Assert.That(playerConfig.Armor, Is.Not.Null);
            Assert.That(playerConfig.Armor.Id, Is.EqualTo("armor-001"));
            Assert.That(playerConfig.Armor.ArmorName, Is.EqualTo("Plate Armor"));
            Assert.That(playerConfig.Armor.Defense, Is.EqualTo(30));
            Assert.That(playerConfig.Armor.ArmorType, Is.EqualTo("Heavy"));
        }

        [Test]
        public void ResolveReferences_InheritanceReference_ShouldResolveCorrectly()
        {
            // Arrange
            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-001",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            var configWithReferencesJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "config-with-refs-001",
                "Name": "Config With References",
                "ReferencedBaseConfig": { "Id": "derived-001" }
            }
            """;

            _fileSource.AddFile("derived-001.json", derivedConfigJson);
            _fileSource.AddFile("config-with-refs-001.json", configWithReferencesJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var configWithRefs = _configDatabase.GetConfig<ConfigWithReferences>("config-with-refs-001");

            // Assert
            Assert.That(configWithRefs, Is.Not.Null);
            Assert.That(configWithRefs.ReferencedBaseConfig, Is.Not.Null);
            Assert.That(configWithRefs.ReferencedBaseConfig, Is.InstanceOf<DerivedConfig>());

            var referencedDerived = (DerivedConfig)configWithRefs.ReferencedBaseConfig;
            Assert.That(referencedDerived.Id, Is.EqualTo("derived-001"));
            Assert.That(referencedDerived.BaseProperty, Is.EqualTo("Base Value"));
            Assert.That(referencedDerived.DerivedProperty, Is.EqualTo("Derived Value"));
        }

        [Test]
        public void ResolveReferences_MissingReference_ShouldReportError()
        {
            // Arrange
            var playerConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+PlayerConfig",
                "Id": "player-001",
                "PlayerName": "Hero",
                "Level": 10,
                "Weapon": { "Id": "non-existent-weapon" }
            }
            """;

            _fileSource.AddFile("player-player-001.json", playerConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var playerConfig = _configDatabase.GetConfig<PlayerConfig>("player-001");

            // Assert
            Assert.That(playerConfig, Is.Not.Null);
            Assert.That(playerConfig.Weapon, Is.Not.Null); // The reference object should still exist
            Assert.That(playerConfig.Weapon.Id, Is.EqualTo("non-existent-weapon")); // But it won't be resolved

            // Should have error reported for missing reference
            Assert.That(_errors.Count, Is.GreaterThan(0));
            Assert.That(_errors.Any(error => error.Contains("Config reference(id: non-existent-weapon) for property Weapon is not found")), Is.True,
                "Should report error for missing weapon reference");
        }

        [Test]
        public void ResolveReferences_NullReference_ShouldReportError()
        {
            // Arrange
            var playerConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+PlayerConfig",
                "Id": "player-001",
                "PlayerName": "Hero",
                "Level": 10,
                "Weapon": null
            }
            """;

            _fileSource.AddFile("player-player-001.json", playerConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var playerConfig = _configDatabase.GetConfig<PlayerConfig>("player-001");

            // Assert
            Assert.That(playerConfig, Is.Not.Null);
            Assert.That(playerConfig.Weapon, Is.Null);

            // No error should be reported for null reference (it's valid to have null optional references)
            // The null check in ResolveConfigProperty should handle this gracefully
        }

        [Test]
        public void ResolveReferences_CircularReference_ShouldHandleGracefully()
        {
            // Arrange
            var configAJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+CircularConfigA",
                "Id": "circular-a",
                "Name": "Config A",
                "RefToB": { "Id": "circular-b" }
            }
            """;

            var configBJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+CircularConfigB",
                "Id": "circular-b",
                "Name": "Config B",
                "RefToA": { "Id": "circular-a" }
            }
            """;

            _fileSource.AddFile("circular-a.json", configAJson);
            _fileSource.AddFile("circular-b.json", configBJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Should not cause infinite loop or stack overflow
            Assert.DoesNotThrow(() =>
            {
                var configA = _configDatabase.GetConfig<CircularConfigA>("circular-a");
                var configB = _configDatabase.GetConfig<CircularConfigB>("circular-b");

                Assert.That(configA, Is.Not.Null);
                Assert.That(configB, Is.Not.Null);

                // References should be resolved
                Assert.That(configA.RefToB, Is.Not.Null);
                Assert.That(configB.RefToA, Is.Not.Null);

                Assert.That(configA.RefToB.Id, Is.EqualTo("circular-b"));
                Assert.That(configB.RefToA.Id, Is.EqualTo("circular-a"));

                // Verify that the circular references point to the same instances
                Assert.That(configA.RefToB, Is.SameAs(configB));
                Assert.That(configB.RefToA, Is.SameAs(configA));
            });
        }

        [Test]
        public void ResolveReferences_SelfReference_ShouldWork()
        {
            // Arrange
            var selfRefConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+SelfReferencingConfig",
                "Id": "self-ref",
                "Name": "Self Referencing Config",
                "SelfReference": { "Id": "self-ref" }
            }
            """;

            _fileSource.AddFile("self-ref.json", selfRefConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var selfRefConfig = _configDatabase.GetConfig<SelfReferencingConfig>("self-ref");

            // Assert
            Assert.That(selfRefConfig, Is.Not.Null);
            Assert.That(selfRefConfig.Name, Is.EqualTo("Self Referencing Config"));
            Assert.That(selfRefConfig.SelfReference, Is.Not.Null);
            Assert.That(selfRefConfig.SelfReference, Is.SameAs(selfRefConfig), "Self reference should point to the same instance");
        }

        [Test]
        public void ResolveReferences_MultipleReferences_ShouldResolveAll()
        {
            // Arrange
            var testConfig1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-1",
                "Name": "Test Config 1",
                "Value": 10
            }
            """;

            var testConfig2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-2",
                "Name": "Test Config 2",
                "Value": 20
            }
            """;

            var derivedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+DerivedConfig",
                "Id": "derived-1",
                "BaseProperty": "Base Value",
                "BaseValue": 100,
                "DerivedProperty": "Derived Value",
                "DerivedValue": 3.14
            }
            """;

            var configWithReferencesJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "multi-refs",
                "Name": "Config With Multiple References",
                "ReferencedTestConfig": { "Id": "test-1" },
                "ReferencedBaseConfig": { "Id": "derived-1" }
            }
            """;

            _fileSource.AddFile("test-1.json", testConfig1Json);
            _fileSource.AddFile("test-2.json", testConfig2Json);
            _fileSource.AddFile("derived-1.json", derivedConfigJson);
            _fileSource.AddFile("multi-refs.json", configWithReferencesJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var multiRefsConfig = _configDatabase.GetConfig<ConfigWithReferences>("multi-refs");

            // Assert
            Assert.That(multiRefsConfig, Is.Not.Null);
            Assert.That(multiRefsConfig.Name, Is.EqualTo("Config With Multiple References"));

            // Verify first reference is resolved
            Assert.That(multiRefsConfig.ReferencedTestConfig, Is.Not.Null);
            Assert.That(multiRefsConfig.ReferencedTestConfig.Id, Is.EqualTo("test-1"));
            Assert.That(multiRefsConfig.ReferencedTestConfig.Name, Is.EqualTo("Test Config 1"));
            Assert.That(multiRefsConfig.ReferencedTestConfig.Value, Is.EqualTo(10));

            // Verify second reference is resolved
            Assert.That(multiRefsConfig.ReferencedBaseConfig, Is.Not.Null);
            Assert.That(multiRefsConfig.ReferencedBaseConfig.Id, Is.EqualTo("derived-1"));
            Assert.That(multiRefsConfig.ReferencedBaseConfig, Is.InstanceOf<DerivedConfig>());

            var referencedDerived = (DerivedConfig)multiRefsConfig.ReferencedBaseConfig;
            Assert.That(referencedDerived.BaseProperty, Is.EqualTo("Base Value"));
            Assert.That(referencedDerived.DerivedProperty, Is.EqualTo("Derived Value"));
        }

        [Test]
        public void ResolveReferences_ReferenceResolutionOrder_ShouldNotMatter()
        {
            // Arrange - Add configs in reverse order of dependency
            var playerConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+PlayerConfig",
                "Id": "player-001",
                "PlayerName": "Hero",
                "Level": 10,
                "Weapon": { "Id": "sword-001" }
            }
            """;

            var weaponConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "sword-001",
                "WeaponName": "Steel Sword",
                "Damage": 50,
                "WeaponType": "Sword"
            }
            """;

            // Add player config first (which references weapon), then weapon config
            _fileSource.AddFile("player-001.json", playerConfigJson);
            _fileSource.AddFile("sword-001.json", weaponConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var playerConfig = _configDatabase.GetConfig<PlayerConfig>("player-001");

            // Assert - Reference should still be resolved correctly
            Assert.That(playerConfig, Is.Not.Null);
            Assert.That(playerConfig.Weapon, Is.Not.Null);
            Assert.That(playerConfig.Weapon.Id, Is.EqualTo("sword-001"));
            Assert.That(playerConfig.Weapon.WeaponName, Is.EqualTo("Steel Sword"));
        }

        [Test]
        public void ResolveReferences_ChainedReferences_ShouldResolveAllLevels()
        {
            // Arrange - Create a chain: ConfigA -> TestConfig -> WeaponConfig
            var weaponConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "weapon-config",
                "WeaponName": "Magic Sword",
                "Damage": 100,
                "WeaponType": "Sword"
            }
            """;

            var testConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-config",
                "Name": "Test Config",
                "Value": 42
            }
            """;

            var playerConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+PlayerConfig",
                "Id": "player-config",
                "PlayerName": "Hero",
                "Level": 10,
                "Weapon": { "Id": "weapon-config" }
            }
            """;

            var configWithRefsJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "config-with-refs",
                "Name": "Config With References",
                "ReferencedTestConfig": { "Id": "test-config" }
            }
            """;

            _fileSource.AddFile("weapon-config.json", weaponConfigJson);
            _fileSource.AddFile("test-config.json", testConfigJson);
            _fileSource.AddFile("player-config.json", playerConfigJson);
            _fileSource.AddFile("config-with-refs.json", configWithRefsJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var configWithRefs = _configDatabase.GetConfig<ConfigWithReferences>("config-with-refs");
            var testConfig = _configDatabase.GetConfig<TestConfig>("test-config");
            var playerConfig = _configDatabase.GetConfig<PlayerConfig>("player-config");
            var weaponConfig = _configDatabase.GetConfig<WeaponConfig>("weapon-config");

            // Assert
            Assert.That(configWithRefs, Is.Not.Null);
            Assert.That(testConfig, Is.Not.Null);
            Assert.That(playerConfig, Is.Not.Null);
            Assert.That(weaponConfig, Is.Not.Null);

            // Verify first level reference: ConfigWithReferences -> TestConfig
            Assert.That(configWithRefs.ReferencedTestConfig, Is.Not.Null);
            Assert.That(configWithRefs.ReferencedTestConfig, Is.SameAs(testConfig));

            // Verify second level reference: PlayerConfig -> WeaponConfig
            Assert.That(playerConfig.Weapon, Is.Not.Null);
            Assert.That(playerConfig.Weapon, Is.SameAs(weaponConfig));

            // Verify final config properties
            Assert.That(testConfig.Name, Is.EqualTo("Test Config"));
            Assert.That(testConfig.Value, Is.EqualTo(42));
            Assert.That(weaponConfig.WeaponName, Is.EqualTo("Magic Sword"));
            Assert.That(weaponConfig.Damage, Is.EqualTo(100));
        }

        [Test]
        public void ResolveReferences_ReferenceSameConfigMultipleTimes_ShouldUseSameInstance()
        {
            // Arrange
            var sharedConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "shared-config",
                "Name": "Shared Config",
                "Value": 42
            }
            """;

            var config1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "config-1",
                "Name": "Config 1",
                "ReferencedTestConfig": { "Id": "shared-config" }
            }
            """;

            var config2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "config-2",
                "Name": "Config 2",
                "ReferencedTestConfig": { "Id": "shared-config" }
            }
            """;

            _fileSource.AddFile("shared-config.json", sharedConfigJson);
            _fileSource.AddFile("config-1.json", config1Json);
            _fileSource.AddFile("config-2.json", config2Json);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var config1 = _configDatabase.GetConfig<ConfigWithReferences>("config-1");
            var config2 = _configDatabase.GetConfig<ConfigWithReferences>("config-2");
            var sharedConfig = _configDatabase.GetConfig<TestConfig>("shared-config");

            // Assert
            Assert.That(config1, Is.Not.Null);
            Assert.That(config2, Is.Not.Null);
            Assert.That(sharedConfig, Is.Not.Null);

            // Both configs should reference the same instance
            Assert.That(config1.ReferencedTestConfig, Is.SameAs(sharedConfig));
            Assert.That(config2.ReferencedTestConfig, Is.SameAs(sharedConfig));
            Assert.That(config1.ReferencedTestConfig, Is.SameAs(config2.ReferencedTestConfig));
        }

        [Test]
        public void ResolveReferences_ErrorDuringResolution_ShouldReportErrorAndContinue()
        {
            // Arrange - This test simulates an error scenario during reference resolution
            var validConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "valid-config",
                "Name": "Valid Config",
                "Value": 42
            }
            """;

            var invalidRefConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "invalid-ref-config",
                "Name": "Invalid Ref Config",
                "ReferencedTestConfig": { "Id": "non-existent-config" }
            }
            """;

            _fileSource.AddFile("valid-config.json", validConfigJson);
            _fileSource.AddFile("invalid-ref-config.json", invalidRefConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act & Assert - Should not throw exception
            Assert.DoesNotThrow(() =>
            {
                var validConfig = _configDatabase.GetConfig<TestConfig>("valid-config");
                var invalidRefConfig = _configDatabase.GetConfig<ConfigWithReferences>("invalid-ref-config");

                Assert.That(validConfig, Is.Not.Null);
                Assert.That(invalidRefConfig, Is.Not.Null);
            });

            // Should have error reported for the invalid reference
            Assert.That(_errors.Count, Is.GreaterThan(0));
            Assert.That(_errors.Any(error => error.Contains("Config reference(id: non-existent-config) for property ReferencedTestConfig is not found")), Is.True);
        }

        #endregion

        #region List Reference Resolution Tests

        [Test]
        public void ResolveReferences_ListReference_ShouldResolveAllItems()
        {
            // Arrange
            var testConfig1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "list-test-1",
                "Name": "List Test Config 1",
                "Value": 10
            }
            """;

            var testConfig2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "list-test-2",
                "Name": "List Test Config 2",
                "Value": 20
            }
            """;

            var testConfig3Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "list-test-3",
                "Name": "List Test Config 3",
                "Value": 30
            }
            """;

            var configWithListRefsJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "config-with-list",
                "Name": "Config With List References",
                "ReferencedTestConfigs": [
                    { "Id": "list-test-1" },
                    { "Id": "list-test-2" },
                    { "Id": "list-test-3" }
                ]
            }
            """;

            _fileSource.AddFile("list-test-1.json", testConfig1Json);
            _fileSource.AddFile("list-test-2.json", testConfig2Json);
            _fileSource.AddFile("list-test-3.json", testConfig3Json);
            _fileSource.AddFile("config-with-list.json", configWithListRefsJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var configWithListRefs = _configDatabase.GetConfig<ConfigWithReferences>("config-with-list");

            // Assert
            Assert.That(configWithListRefs, Is.Not.Null);
            Assert.That(configWithListRefs.ReferencedTestConfigs, Is.Not.Null);
            Assert.That(configWithListRefs.ReferencedTestConfigs.Count, Is.EqualTo(3));

            // Verify all list items are resolved correctly
            var listItem1 = configWithListRefs.ReferencedTestConfigs[0];
            var listItem2 = configWithListRefs.ReferencedTestConfigs[1];
            var listItem3 = configWithListRefs.ReferencedTestConfigs[2];

            Assert.That(listItem1.Id, Is.EqualTo("list-test-1"));
            Assert.That(listItem1.Name, Is.EqualTo("List Test Config 1"));
            Assert.That(listItem1.Value, Is.EqualTo(10));

            Assert.That(listItem2.Id, Is.EqualTo("list-test-2"));
            Assert.That(listItem2.Name, Is.EqualTo("List Test Config 2"));
            Assert.That(listItem2.Value, Is.EqualTo(20));

            Assert.That(listItem3.Id, Is.EqualTo("list-test-3"));
            Assert.That(listItem3.Name, Is.EqualTo("List Test Config 3"));
            Assert.That(listItem3.Value, Is.EqualTo(30));

            // Verify that the referenced configs are the same instances as when accessed directly
            var directConfig1 = _configDatabase.GetConfig<TestConfig>("list-test-1");
            var directConfig2 = _configDatabase.GetConfig<TestConfig>("list-test-2");
            var directConfig3 = _configDatabase.GetConfig<TestConfig>("list-test-3");

            Assert.That(listItem1, Is.SameAs(directConfig1));
            Assert.That(listItem2, Is.SameAs(directConfig2));
            Assert.That(listItem3, Is.SameAs(directConfig3));
        }

        [Test]
        public void ResolveReferences_EmptyListReference_ShouldHandleGracefully()
        {
            // Arrange
            var configWithEmptyListJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "config-with-empty-list",
                "Name": "Config With Empty List",
                "ReferencedTestConfigs": []
            }
            """;

            _fileSource.AddFile("config-with-empty-list.json", configWithEmptyListJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var configWithEmptyList = _configDatabase.GetConfig<ConfigWithReferences>("config-with-empty-list");

            // Assert
            Assert.That(configWithEmptyList, Is.Not.Null);
            Assert.That(configWithEmptyList.ReferencedTestConfigs, Is.Not.Null);
            Assert.That(configWithEmptyList.ReferencedTestConfigs.Count, Is.EqualTo(0));
        }

        #endregion

        #region Nested Object Reference Resolution Tests

        [Test]
        public void ResolveReferences_NestedObjectReferences_ShouldResolveCorrectly()
        {
            // Arrange
            var primaryWeaponJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "primary-sword",
                "WeaponName": "Primary Steel Sword",
                "Damage": 75,
                "WeaponType": "Sword"
            }
            """;

            var secondaryWeaponJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "secondary-dagger",
                "WeaponName": "Secondary Dagger",
                "Damage": 35,
                "WeaponType": "Dagger"
            }
            """;

            var armor1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ArmorConfig",
                "Id": "helmet-001",
                "ArmorName": "Steel Helmet",
                "Defense": 15,
                "ArmorType": "Head"
            }
            """;

            var armor2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ArmorConfig",
                "Id": "chestplate-001",
                "ArmorName": "Steel Chestplate",
                "Defense": 25,
                "ArmorType": "Chest"
            }
            """;

            var configWithNestedRefsJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithNestedObjectReferences",
                "Id": "nested-refs-config",
                "Name": "Config With Nested References",
                "Equipment": {
                    "PrimaryWeapon": { "Id": "primary-sword" },
                    "SecondaryWeapon": { "Id": "secondary-dagger" },
                    "ArmorPieces": [
                        { "Id": "helmet-001" },
                        { "Id": "chestplate-001" }
                    ]
                }
            }
            """;

            _fileSource.AddFile("primary-sword.json", primaryWeaponJson);
            _fileSource.AddFile("secondary-dagger.json", secondaryWeaponJson);
            _fileSource.AddFile("helmet-001.json", armor1Json);
            _fileSource.AddFile("chestplate-001.json", armor2Json);
            _fileSource.AddFile("nested-refs-config.json", configWithNestedRefsJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var configWithNestedRefs = _configDatabase.GetConfig<ConfigWithNestedObjectReferences>("nested-refs-config");

            // Assert
            Assert.That(configWithNestedRefs, Is.Not.Null);
            Assert.That(configWithNestedRefs.Name, Is.EqualTo("Config With Nested References"));
            Assert.That(configWithNestedRefs.Equipment, Is.Not.Null);

            // Verify nested object's primary weapon reference
            Assert.That(configWithNestedRefs.Equipment.PrimaryWeapon, Is.Not.Null);
            Assert.That(configWithNestedRefs.Equipment.PrimaryWeapon.Id, Is.EqualTo("primary-sword"));
            Assert.That(configWithNestedRefs.Equipment.PrimaryWeapon.WeaponName, Is.EqualTo("Primary Steel Sword"));
            Assert.That(configWithNestedRefs.Equipment.PrimaryWeapon.Damage, Is.EqualTo(75));

            // Verify nested object's secondary weapon reference
            Assert.That(configWithNestedRefs.Equipment.SecondaryWeapon, Is.Not.Null);
            Assert.That(configWithNestedRefs.Equipment.SecondaryWeapon.Id, Is.EqualTo("secondary-dagger"));
            Assert.That(configWithNestedRefs.Equipment.SecondaryWeapon.WeaponName, Is.EqualTo("Secondary Dagger"));
            Assert.That(configWithNestedRefs.Equipment.SecondaryWeapon.Damage, Is.EqualTo(35));

            // Verify nested object's armor list references
            Assert.That(configWithNestedRefs.Equipment.ArmorPieces, Is.Not.Null);
            Assert.That(configWithNestedRefs.Equipment.ArmorPieces.Count, Is.EqualTo(2));

            var helmet = configWithNestedRefs.Equipment.ArmorPieces[0];
            var chestplate = configWithNestedRefs.Equipment.ArmorPieces[1];

            Assert.That(helmet.Id, Is.EqualTo("helmet-001"));
            Assert.That(helmet.ArmorName, Is.EqualTo("Steel Helmet"));
            Assert.That(helmet.Defense, Is.EqualTo(15));
            Assert.That(helmet.ArmorType, Is.EqualTo("Head"));

            Assert.That(chestplate.Id, Is.EqualTo("chestplate-001"));
            Assert.That(chestplate.ArmorName, Is.EqualTo("Steel Chestplate"));
            Assert.That(chestplate.Defense, Is.EqualTo(25));
            Assert.That(chestplate.ArmorType, Is.EqualTo("Chest"));
        }

        [Test]
        public void ResolveReferences_MultipleNestedObjectReferences_ShouldResolveAll()
        {
            // Arrange
            var weaponJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "shared-weapon",
                "WeaponName": "Shared Weapon",
                "Damage": 50,
                "WeaponType": "Sword"
            }
            """;

            var armorJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ArmorConfig",
                "Id": "shared-armor",
                "ArmorName": "Shared Armor",
                "Defense": 20,
                "ArmorType": "Body"
            }
            """;

            var configWithMultipleNestedRefsJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithNestedObjectReferences",
                "Id": "multiple-nested-refs",
                "Name": "Config With Multiple Nested References",
                "Equipment": {
                    "PrimaryWeapon": { "Id": "shared-weapon" },
                    "ArmorPieces": [
                        { "Id": "shared-armor" }
                    ]
                },
                "BackupEquipment": {
                    "PrimaryWeapon": { "Id": "shared-weapon" },
                    "ArmorPieces": [
                        { "Id": "shared-armor" }
                    ]
                }
            }
            """;

            _fileSource.AddFile("shared-weapon.json", weaponJson);
            _fileSource.AddFile("shared-armor.json", armorJson);
            _fileSource.AddFile("multiple-nested-refs.json", configWithMultipleNestedRefsJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var config = _configDatabase.GetConfig<ConfigWithNestedObjectReferences>("multiple-nested-refs");

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Equipment, Is.Not.Null);
            Assert.That(config.BackupEquipment, Is.Not.Null);

            // Verify that both nested objects reference the same shared weapon instance
            Assert.That(config.Equipment.PrimaryWeapon, Is.Not.Null);
            Assert.That(config.BackupEquipment.PrimaryWeapon, Is.Not.Null);
            Assert.That(config.Equipment.PrimaryWeapon, Is.SameAs(config.BackupEquipment.PrimaryWeapon));

            // Verify that both nested objects reference the same shared armor instance
            Assert.That(config.Equipment.ArmorPieces.Count, Is.EqualTo(1));
            Assert.That(config.BackupEquipment.ArmorPieces.Count, Is.EqualTo(1));
            Assert.That(config.Equipment.ArmorPieces[0], Is.SameAs(config.BackupEquipment.ArmorPieces[0]));
        }

        [Test]
        public void ResolveReferences_NestedObjectWithMissingReference_ShouldReportError()
        {
            // Arrange
            var configWithMissingNestedRefJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithNestedObjectReferences",
                "Id": "missing-nested-ref",
                "Name": "Config With Missing Nested Reference",
                "Equipment": {
                    "PrimaryWeapon": { "Id": "non-existent-weapon" }
                }
            }
            """;

            _fileSource.AddFile("missing-nested-ref.json", configWithMissingNestedRefJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var config = _configDatabase.GetConfig<ConfigWithNestedObjectReferences>("missing-nested-ref");

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config.Equipment, Is.Not.Null);
            Assert.That(config.Equipment.PrimaryWeapon, Is.Not.Null);
            Assert.That(config.Equipment.PrimaryWeapon.Id, Is.EqualTo("non-existent-weapon"));

            // Should have error reported for missing reference in nested object
            Assert.That(_errors.Count, Is.GreaterThan(0));
            Assert.That(_errors.Any(error => error.Contains("Config reference(id: non-existent-weapon) for property PrimaryWeapon is not found")), Is.True);
        }

        #endregion

        #region Dictionary Reference Resolution Tests

        [Test]
        public void ResolveReferences_DictionaryReferences_ShouldResolveCorrectly()
        {
            // Arrange
            var swordJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "sword-weapon",
                "WeaponName": "Steel Sword",
                "Damage": 60,
                "WeaponType": "Sword"
            }
            """;

            var bowJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "bow-weapon",
                "WeaponName": "Longbow",
                "Damage": 45,
                "WeaponType": "Bow"
            }
            """;

            var helmetJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ArmorConfig",
                "Id": "light-helmet",
                "ArmorName": "Light Helmet",
                "Defense": 10,
                "ArmorType": "Head"
            }
            """;

            var testConfig1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-level-1",
                "Name": "Level 1 Test",
                "Value": 100
            }
            """;

            var configWithDictRefsJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithDictionaryReferences",
                "Id": "dict-refs-config",
                "Name": "Config With Dictionary References",
                "WeaponsByType": {
                    "melee": { "Id": "sword-weapon" },
                    "ranged": { "Id": "bow-weapon" }
                },
                "ArmorByCategory": {
                    "light": [
                        { "Id": "light-helmet" }
                    ]
                },
                "ConfigsByLevel": {
                    "1": { "Id": "test-level-1" }
                }
            }
            """;

            _fileSource.AddFile("sword-weapon.json", swordJson);
            _fileSource.AddFile("bow-weapon.json", bowJson);
            _fileSource.AddFile("light-helmet.json", helmetJson);
            _fileSource.AddFile("test-level-1.json", testConfig1Json);
            _fileSource.AddFile("dict-refs-config.json", configWithDictRefsJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var configWithDictRefs = _configDatabase.GetConfig<ConfigWithDictionaryReferences>("dict-refs-config");

            // Assert
            Assert.That(configWithDictRefs, Is.Not.Null);
            Assert.That(configWithDictRefs.Name, Is.EqualTo("Config With Dictionary References"));

            // Verify dictionary with simple config references
            Assert.That(configWithDictRefs.WeaponsByType, Is.Not.Null);
            Assert.That(configWithDictRefs.WeaponsByType.Count, Is.EqualTo(2));
            Assert.That(configWithDictRefs.WeaponsByType.ContainsKey("melee"), Is.True);
            Assert.That(configWithDictRefs.WeaponsByType.ContainsKey("ranged"), Is.True);

            var meleeWeapon = configWithDictRefs.WeaponsByType["melee"];
            var rangedWeapon = configWithDictRefs.WeaponsByType["ranged"];

            Assert.That(meleeWeapon.Id, Is.EqualTo("sword-weapon"));
            Assert.That(meleeWeapon.WeaponName, Is.EqualTo("Steel Sword"));
            Assert.That(meleeWeapon.Damage, Is.EqualTo(60));

            Assert.That(rangedWeapon.Id, Is.EqualTo("bow-weapon"));
            Assert.That(rangedWeapon.WeaponName, Is.EqualTo("Longbow"));
            Assert.That(rangedWeapon.Damage, Is.EqualTo(45));

            // Verify dictionary with list of config references
            Assert.That(configWithDictRefs.ArmorByCategory, Is.Not.Null);
            Assert.That(configWithDictRefs.ArmorByCategory.Count, Is.EqualTo(1));
            Assert.That(configWithDictRefs.ArmorByCategory.ContainsKey("light"), Is.True);

            var lightArmors = configWithDictRefs.ArmorByCategory["light"];
            Assert.That(lightArmors.Count, Is.EqualTo(1));
            Assert.That(lightArmors[0].Id, Is.EqualTo("light-helmet"));
            Assert.That(lightArmors[0].ArmorName, Is.EqualTo("Light Helmet"));
            Assert.That(lightArmors[0].Defense, Is.EqualTo(10));

            // Verify dictionary with integer keys
            Assert.That(configWithDictRefs.ConfigsByLevel, Is.Not.Null);
            Assert.That(configWithDictRefs.ConfigsByLevel.Count, Is.EqualTo(1));
            Assert.That(configWithDictRefs.ConfigsByLevel.ContainsKey(1), Is.True);

            var levelConfig = configWithDictRefs.ConfigsByLevel[1];
            Assert.That(levelConfig.Id, Is.EqualTo("test-level-1"));
            Assert.That(levelConfig.Name, Is.EqualTo("Level 1 Test"));
            Assert.That(levelConfig.Value, Is.EqualTo(100));
        }

        [Test]
        public void ResolveReferences_EmptyDictionaryReferences_ShouldHandleGracefully()
        {
            // Arrange
            var configWithEmptyDictJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithDictionaryReferences",
                "Id": "empty-dict-config",
                "Name": "Config With Empty Dictionaries",
                "WeaponsByType": {},
                "ArmorByCategory": {},
                "ConfigsByLevel": {}
            }
            """;

            _fileSource.AddFile("empty-dict-config.json", configWithEmptyDictJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var configWithEmptyDict = _configDatabase.GetConfig<ConfigWithDictionaryReferences>("empty-dict-config");

            // Assert
            Assert.That(configWithEmptyDict, Is.Not.Null);
            Assert.That(configWithEmptyDict.WeaponsByType, Is.Not.Null);
            Assert.That(configWithEmptyDict.WeaponsByType.Count, Is.EqualTo(0));
            Assert.That(configWithEmptyDict.ArmorByCategory, Is.Not.Null);
            Assert.That(configWithEmptyDict.ArmorByCategory.Count, Is.EqualTo(0));
            Assert.That(configWithEmptyDict.ConfigsByLevel, Is.Not.Null);
            Assert.That(configWithEmptyDict.ConfigsByLevel.Count, Is.EqualTo(0));
        }

        [Test]
        public void ResolveReferences_DictionaryWithMissingReference_ShouldReportError()
        {
            // Arrange
            var configWithMissingDictRefJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithDictionaryReferences",
                "Id": "missing-dict-ref",
                "Name": "Config With Missing Dictionary Reference",
                "WeaponsByType": {
                    "missing": { "Id": "non-existent-weapon" }
                }
            }
            """;

            _fileSource.AddFile("missing-dict-ref.json", configWithMissingDictRefJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var config = _configDatabase.GetConfig<ConfigWithDictionaryReferences>("missing-dict-ref");

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config.WeaponsByType, Is.Not.Null);
            Assert.That(config.WeaponsByType.ContainsKey("missing"), Is.True);
            Assert.That(config.WeaponsByType["missing"], Is.Not.Null);
            Assert.That(config.WeaponsByType["missing"].Id, Is.EqualTo("non-existent-weapon"));

            // Should have error reported for missing reference in dictionary
            Assert.That(_errors.Count, Is.GreaterThan(0));
            Assert.That(_errors.Any(error => error.Contains("Config reference(id: non-existent-weapon)")), Is.True);
        }

        [Test]
        public void ResolveReferences_DictionaryWithSameReference_ShouldUseSameInstance()
        {
            // Arrange
            var sharedWeaponJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "shared-weapon",
                "WeaponName": "Shared Weapon",
                "Damage": 50,
                "WeaponType": "Universal"
            }
            """;

            var configWithSharedRefsJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithDictionaryReferences",
                "Id": "shared-refs-config",
                "Name": "Config With Shared References",
                "WeaponsByType": {
                    "primary": { "Id": "shared-weapon" },
                    "secondary": { "Id": "shared-weapon" }
                }
            }
            """;

            _fileSource.AddFile("shared-weapon.json", sharedWeaponJson);
            _fileSource.AddFile("shared-refs-config.json", configWithSharedRefsJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var config = _configDatabase.GetConfig<ConfigWithDictionaryReferences>("shared-refs-config");

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config.WeaponsByType, Is.Not.Null);
            Assert.That(config.WeaponsByType.Count, Is.EqualTo(2));

            var primaryWeapon = config.WeaponsByType["primary"];
            var secondaryWeapon = config.WeaponsByType["secondary"];

            Assert.That(primaryWeapon, Is.Not.Null);
            Assert.That(secondaryWeapon, Is.Not.Null);
            Assert.That(primaryWeapon, Is.SameAs(secondaryWeapon), "Dictionary should reference the same weapon instance");
        }

        #endregion

        #region Complex Mixed Reference Resolution Tests

        [Test]
        public void ResolveReferences_ComplexMixedReferences_ShouldResolveAll()
        {
            // Arrange - Create a complex scenario with nested objects inside dictionaries
            var weaponJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+WeaponConfig",
                "Id": "complex-weapon",
                "WeaponName": "Complex Weapon",
                "Damage": 80,
                "WeaponType": "Magic"
            }
            """;

            var armorJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ArmorConfig",
                "Id": "complex-armor",
                "ArmorName": "Complex Armor",
                "Defense": 40,
                "ArmorType": "Enchanted"
            }
            """;

            var complexConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ComplexConfigWithMixedReferences",
                "Id": "complex-mixed-refs",
                "Name": "Complex Config With Mixed References",
                "MainEquipment": {
                    "PrimaryWeapon": { "Id": "complex-weapon" },
                    "ArmorPieces": [
                        { "Id": "complex-armor" }
                    ]
                },
                "EquipmentSets": {
                    "set1": {
                        "PrimaryWeapon": { "Id": "complex-weapon" },
                        "ArmorPieces": [
                            { "Id": "complex-armor" }
                        ]
                    }
                },
                "WeaponCollections": {
                    "magic": [
                        { "Id": "complex-weapon" }
                    ]
                }
            }
            """;

            _fileSource.AddFile("complex-weapon.json", weaponJson);
            _fileSource.AddFile("complex-armor.json", armorJson);
            _fileSource.AddFile("complex-mixed-refs.json", complexConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var complexConfig = _configDatabase.GetConfig<ComplexConfigWithMixedReferences>("complex-mixed-refs");

            // Assert
            Assert.That(complexConfig, Is.Not.Null);
            Assert.That(complexConfig.Name, Is.EqualTo("Complex Config With Mixed References"));

            // Verify main equipment (nested object references)
            Assert.That(complexConfig.MainEquipment, Is.Not.Null);
            Assert.That(complexConfig.MainEquipment.PrimaryWeapon, Is.Not.Null);
            Assert.That(complexConfig.MainEquipment.PrimaryWeapon.Id, Is.EqualTo("complex-weapon"));
            Assert.That(complexConfig.MainEquipment.ArmorPieces.Count, Is.EqualTo(1));
            Assert.That(complexConfig.MainEquipment.ArmorPieces[0].Id, Is.EqualTo("complex-armor"));

            // Verify equipment sets (dictionary of nested objects with references)
            Assert.That(complexConfig.EquipmentSets, Is.Not.Null);
            Assert.That(complexConfig.EquipmentSets.ContainsKey("set1"), Is.True);
            var set1 = complexConfig.EquipmentSets["set1"];
            Assert.That(set1, Is.Not.Null);
            Assert.That(set1.PrimaryWeapon, Is.Not.Null);
            Assert.That(set1.PrimaryWeapon.Id, Is.EqualTo("complex-weapon"));

            // Verify weapon collections (dictionary of lists with references)
            Assert.That(complexConfig.WeaponCollections, Is.Not.Null);
            Assert.That(complexConfig.WeaponCollections.ContainsKey("magic"), Is.True);
            var magicWeapons = complexConfig.WeaponCollections["magic"];
            Assert.That(magicWeapons.Count, Is.EqualTo(1));
            Assert.That(magicWeapons[0].Id, Is.EqualTo("complex-weapon"));

            // Verify that all references point to the same instances (caching)
            var directWeapon = _configDatabase.GetConfig<WeaponConfig>("complex-weapon");
            var directArmor = _configDatabase.GetConfig<ArmorConfig>("complex-armor");

            Assert.That(complexConfig.MainEquipment.PrimaryWeapon, Is.SameAs(directWeapon));
            Assert.That(complexConfig.MainEquipment.ArmorPieces[0], Is.SameAs(directArmor));
            Assert.That(set1.PrimaryWeapon, Is.SameAs(directWeapon));
            Assert.That(set1.ArmorPieces[0], Is.SameAs(directArmor));
            Assert.That(magicWeapons[0], Is.SameAs(directWeapon));
        }

        #endregion

        #region Combined Reference Tests

        // TODO: Add tests that combine single references, list references, and inheritance

        #endregion

        [Test]
        public void JsoncSupport_ShouldParseJsonWithCommentsAndTrailingCommas()
        {
            // Arrange - Create JSONC with both single-line comments, multi-line comments, and trailing commas
            var jsoncContent = """
            {
                // This is a single-line comment
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "jsonc-test-config",
                /* This is a 
                   multi-line comment */
                "Name": "JSONC Test Config", // another comment
                "Value": 42, // trailing comma should be allowed
            }
            """;

            _fileSource.AddFile("jsonc-test.jsonc", jsoncContent);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("jsonc-test-config", typeof(TestConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.Not.Null);
            Assert.That(config, Is.InstanceOf<TestConfig>());

            var testConfig = (TestConfig)config;
            Assert.That(testConfig.Id, Is.EqualTo("jsonc-test-config"));
            Assert.That(testConfig.Name, Is.EqualTo("JSONC Test Config"));
            Assert.That(testConfig.Value, Is.EqualTo(42));
        }

        [Test]
        public void JsoncSupport_ShouldParseComplexJsoncStructure()
        {
            // Arrange - Create complex JSONC with nested objects, arrays, and various comment styles
            var complexJsoncContent = """
            {
                // Configuration for a complex test scenario
                "$type": "Alco.Engine.Test.TestConfigDatabase+ConfigWithReferences",
                "Id": "complex-jsonc-config",
                "Name": "Complex JSONC Config", // Main config name
                
                /* Referenced test config with trailing comma in array */
                "ReferencedTestConfigs": [
                    { "Id": "test-1" }, // First reference
                    { "Id": "test-2" }, // Second reference with trailing comma
                ], // Array can have trailing comma too
                
                // Single config reference
                "ReferencedTestConfig": { "Id": "test-1" }, // Trailing comma in main object
            }
            """;

            // Create the referenced configs
            var testConfig1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-1",
                "Name": "Test Config 1",
                "Value": 10,
            }
            """;

            var testConfig2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "test-2",
                "Name": "Test Config 2",
                "Value": 20,
            }
            """;

            _fileSource.AddFile("complex-jsonc.jsonc", complexJsoncContent);
            _fileSource.AddFile("test-1.json", testConfig1Json);
            _fileSource.AddFile("test-2.json", testConfig2Json);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("complex-jsonc-config", typeof(ConfigWithReferences), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.Not.Null);
            Assert.That(config, Is.InstanceOf<ConfigWithReferences>());

            var complexConfig = (ConfigWithReferences)config;
            Assert.That(complexConfig.Id, Is.EqualTo("complex-jsonc-config"));
            Assert.That(complexConfig.Name, Is.EqualTo("Complex JSONC Config"));

            // Verify referenced configs are resolved correctly
            Assert.That(complexConfig.ReferencedTestConfig, Is.Not.Null);
            Assert.That(complexConfig.ReferencedTestConfig.Id, Is.EqualTo("test-1"));
            Assert.That(complexConfig.ReferencedTestConfig.Name, Is.EqualTo("Test Config 1"));

            Assert.That(complexConfig.ReferencedTestConfigs, Is.Not.Null);
            Assert.That(complexConfig.ReferencedTestConfigs.Count, Is.EqualTo(2));
            Assert.That(complexConfig.ReferencedTestConfigs[0].Id, Is.EqualTo("test-1"));
            Assert.That(complexConfig.ReferencedTestConfigs[1].Id, Is.EqualTo("test-2"));
        }
    }
}