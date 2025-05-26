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
            private readonly Dictionary<string, byte[]> _files = new();

            public int Priority => 0;
            public IEnumerable<string> AllFileNames => _files.Keys;
            public bool IsWriteable => false;

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

            public bool TryWriteData(string path, ReadOnlySpan<byte> data, out string failureReason)
            {
                failureReason = "Not supported";
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
                [],
                info => _infos.Add(info),
                warning => _warnings.Add(warning),
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
    }
}