using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

            public bool TryGetStream(string path, [NotNullWhen(true)] out Stream stream, [NotNullWhen(false)] out string failureReason)
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
        /// Config class for testing cross-references.
        /// </summary>
        private class CrossReferenceConfig : Configable
        {
            public string Name { get; set; } = string.Empty;
            public ConfigReference<CrossReferenceConfig> Friend { get; set; } = null!;
        }

        /// <summary>
        /// Config class for testing list references.
        /// </summary>
        private class ListReferenceConfig : Configable
        {
            public string Name { get; set; } = string.Empty;
            public List<ConfigReference<TestConfig>> RequiredRefs { get; set; } = new();
            public List<ConfigReferenceOptional<AnotherTestConfig>> OptionalRefs { get; set; } = new();
        }

        /// <summary>
        /// Config class for testing hashset references.
        /// </summary>
        private class HashSetReferenceConfig : Configable
        {
            public string Name { get; set; } = string.Empty;
            public HashSet<ConfigReference<TestConfig>> RequiredRefs { get; set; } = new();
            public HashSet<ConfigReferenceOptional<AnotherTestConfig>> OptionalRefs { get; set; } = new();
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

        #region ConfigReference Tests

        [Test]
        public void ConfigReference_CrossReference_ShouldResolveCorrectly()
        {
            // Arrange - Create two configs that reference each other
            var configAJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+CrossReferenceConfig",
                "Id": "config-a",
                "Name": "Config A",
                "Friend": "config-b"
            }
            """;

            var configBJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+CrossReferenceConfig",
                "Id": "config-b",
                "Name": "Config B",
                "Friend": "config-a"
            }
            """;

            _fileSource.AddFile("config-a.json", configAJson);
            _fileSource.AddFile("config-b.json", configBJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act - Get both configs
            var resultA = _configDatabase.TryGetConfig("config-a", typeof(CrossReferenceConfig), out var configA);
            var resultB = _configDatabase.TryGetConfig("config-b", typeof(CrossReferenceConfig), out var configB);

            // Assert
            Assert.That(resultA, Is.True);
            Assert.That(resultB, Is.True);
            Assert.That(configA, Is.InstanceOf<CrossReferenceConfig>());
            Assert.That(configB, Is.InstanceOf<CrossReferenceConfig>());

            var crossConfigA = (CrossReferenceConfig)configA;
            var crossConfigB = (CrossReferenceConfig)configB;

            // Verify names
            Assert.That(crossConfigA.Name, Is.EqualTo("Config A"));
            Assert.That(crossConfigB.Name, Is.EqualTo("Config B"));

            // Verify cross-references resolve correctly
            Assert.That(crossConfigA.Friend.Config, Is.SameAs(crossConfigB));
            Assert.That(crossConfigB.Friend.Config, Is.SameAs(crossConfigA));

            // Verify the friends reference back correctly
            Assert.That(crossConfigA.Friend.Config.Friend.Config, Is.SameAs(crossConfigA));
            Assert.That(crossConfigB.Friend.Config.Friend.Config, Is.SameAs(crossConfigB));
        }

        [Test]
        public void ConfigReference_CrossReferenceWithSelfReference_ShouldWork()
        {
            // Arrange - Create a config that references itself
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+CrossReferenceConfig",
                "Id": "self-ref-config",
                "Name": "Self Reference Config",
                "Friend": "self-ref-config"
            }
            """;

            _fileSource.AddFile("self-ref-config.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("self-ref-config", typeof(CrossReferenceConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.InstanceOf<CrossReferenceConfig>());

            var crossConfig = (CrossReferenceConfig)config;
            Assert.That(crossConfig.Name, Is.EqualTo("Self Reference Config"));

            // Verify self-reference resolves correctly
            Assert.That(crossConfig.Friend.Config, Is.SameAs(crossConfig));
        }

        [Test]
        public void ConfigReference_CrossReferenceWithInvalidId_ShouldThrowException()
        {
            // Arrange - Create a config that references a non-existent config
            var configJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+CrossReferenceConfig",
                "Id": "invalid-ref-config",
                "Name": "Invalid Reference Config",
                "Friend": "non-existent-config"
            }
            """;

            _fileSource.AddFile("invalid-ref-config.json", configJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act - Get the config
            var result = _configDatabase.TryGetConfig("invalid-ref-config", typeof(CrossReferenceConfig), out var config);

            // Assert - Config itself should be found
            Assert.That(result, Is.True);
            Assert.That(config, Is.InstanceOf<CrossReferenceConfig>());

            var crossConfig = (CrossReferenceConfig)config;
            Assert.That(crossConfig.Name, Is.EqualTo("Invalid Reference Config"));

            // Act & Assert - Accessing the invalid reference should throw
            var exception = Assert.Throws<Exception>(() =>
            {
                var friend = crossConfig.Friend.Config;
            });

            Assert.That(exception.Message, Does.Contain("non-existent-config"));
        }

        [Test]
        public void ConfigReference_ListReferences_ShouldResolveAllReferencesCorrectly()
        {
            // Arrange - Create multiple configs and a config with list of references
            var config1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "list-ref-target-1",
                "Name": "Target Config 1",
                "Value": 10
            }
            """;

            var config2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "list-ref-target-2",
                "Name": "Target Config 2",
                "Value": 20
            }
            """;

            var config3Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "list-ref-target-3",
                "Name": "Target Config 3",
                "Value": 30
            }
            """;

            var optionalConfig1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherTestConfig",
                "Id": "list-optional-target-1",
                "Description": "Optional Target Config 1",
                "Score": 1.5
            }
            """;

            var optionalConfig2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherTestConfig",
                "Id": "list-optional-target-2",
                "Description": "Optional Target Config 2",
                "Score": 2.5
            }
            """;

            var listConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ListReferenceConfig",
                "Id": "list-config",
                "Name": "List Reference Config",
                "RequiredRefs": ["list-ref-target-1", "list-ref-target-2", "list-ref-target-3"],
                "OptionalRefs": ["list-optional-target-1", "list-optional-target-2"]
            }
            """;

            _fileSource.AddFile("list-ref-target-1.json", config1Json);
            _fileSource.AddFile("list-ref-target-2.json", config2Json);
            _fileSource.AddFile("list-ref-target-3.json", config3Json);
            _fileSource.AddFile("list-optional-target-1.json", optionalConfig1Json);
            _fileSource.AddFile("list-optional-target-2.json", optionalConfig2Json);
            _fileSource.AddFile("list-config.json", listConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("list-config", typeof(ListReferenceConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.InstanceOf<ListReferenceConfig>());

            var listConfig = (ListReferenceConfig)config;
            Assert.That(listConfig.Name, Is.EqualTo("List Reference Config"));

            // Verify required references
            Assert.That(listConfig.RequiredRefs, Has.Count.EqualTo(3));
            Assert.That(listConfig.RequiredRefs[0].Config.Value, Is.EqualTo(10));
            Assert.That(listConfig.RequiredRefs[1].Config.Value, Is.EqualTo(20));
            Assert.That(listConfig.RequiredRefs[2].Config.Value, Is.EqualTo(30));

            // Verify optional references - should resolve to valid configs
            Assert.That(listConfig.OptionalRefs, Has.Count.EqualTo(2));
            Assert.That(listConfig.OptionalRefs[0].Config, Is.Not.Null);
            Assert.That(listConfig.OptionalRefs[1].Config, Is.Not.Null);
            Assert.That(listConfig.OptionalRefs[0].Config?.Score, Is.EqualTo(1.5f));
            Assert.That(listConfig.OptionalRefs[1].Config?.Score, Is.EqualTo(2.5f));
        }

        [Test]
        public void ConfigReference_ListReferencesWithEmptyOptionalRefs_ShouldHandleNulls()
        {
            // Arrange - Create configs for testing empty optional references
            var validConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherTestConfig",
                "Id": "valid-optional-config",
                "Description": "Valid Optional Config",
                "Score": 3.5
            }
            """;

            var listConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ListReferenceConfig",
                "Id": "list-config-empty",
                "Name": "List Config With Empty Refs",
                "RequiredRefs": [],
                "OptionalRefs": ["", "valid-optional-config", ""]
            }
            """;

            _fileSource.AddFile("valid-optional-config.json", validConfigJson);
            _fileSource.AddFile("list-config-empty.json", listConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("list-config-empty", typeof(ListReferenceConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.InstanceOf<ListReferenceConfig>());

            var listConfig = (ListReferenceConfig)config;
            Assert.That(listConfig.Name, Is.EqualTo("List Config With Empty Refs"));

            // Verify required references are empty
            Assert.That(listConfig.RequiredRefs, Is.Empty);

            // Verify optional references handle empty IDs correctly
            Assert.That(listConfig.OptionalRefs, Has.Count.EqualTo(3));
            Assert.That(listConfig.OptionalRefs[0].Config, Is.Null); // Empty string returns null
            Assert.That(listConfig.OptionalRefs[1].Config, Is.Not.Null); // Valid config
            Assert.That(listConfig.OptionalRefs[1].Config?.Score, Is.EqualTo(3.5f));
            Assert.That(listConfig.OptionalRefs[2].Config, Is.Null); // Empty string returns null
        }

        [Test]
        public void ConfigReference_ListReferencesWithInvalidRequiredRef_ShouldThrowOnAccess()
        {
            // Arrange - Create a config with an invalid required reference
            var listConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+ListReferenceConfig",
                "Id": "list-config-invalid",
                "Name": "List Config With Invalid Ref",
                "RequiredRefs": ["non-existent-config"],
                "OptionalRefs": []
            }
            """;

            _fileSource.AddFile("list-config-invalid.json", listConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("list-config-invalid", typeof(ListReferenceConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.InstanceOf<ListReferenceConfig>());

            var listConfig = (ListReferenceConfig)config;

            // Act & Assert - Accessing invalid required reference should throw
            var exception = Assert.Throws<Exception>(() =>
            {
                var invalidConfig = listConfig.RequiredRefs[0].Config;
            });

            Assert.That(exception.Message, Does.Contain("non-existent-config"));
        }

        [Test]
        public void ConfigReference_HashSetReferences_ShouldResolveAllReferencesCorrectly()
        {
            // Arrange - Create multiple configs and a config with hashset of references
            var config1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherTestConfig",
                "Id": "hashset-ref-target-1",
                "Description": "Target Config 1",
                "Score": 1.1
            }
            """;

            var config2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+AnotherTestConfig",
                "Id": "hashset-ref-target-2",
                "Description": "Target Config 2",
                "Score": 2.2
            }
            """;

            var config3Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "hashset-ref-target-3",
                "Name": "Target Config 3",
                "Value": 33
            }
            """;

            var hashSetConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+HashSetReferenceConfig",
                "Id": "hashset-config",
                "Name": "HashSet Reference Config",
                "RequiredRefs": ["hashset-ref-target-3"],
                "OptionalRefs": ["hashset-ref-target-1", "hashset-ref-target-2"]
            }
            """;

            _fileSource.AddFile("hashset-ref-target-1.json", config1Json);
            _fileSource.AddFile("hashset-ref-target-2.json", config2Json);
            _fileSource.AddFile("hashset-ref-target-3.json", config3Json);
            _fileSource.AddFile("hashset-config.json", hashSetConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("hashset-config", typeof(HashSetReferenceConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.InstanceOf<HashSetReferenceConfig>());

            var hashSetConfig = (HashSetReferenceConfig)config;
            Assert.That(hashSetConfig.Name, Is.EqualTo("HashSet Reference Config"));

            // Verify required references
            Assert.That(hashSetConfig.RequiredRefs, Has.Count.EqualTo(1));
            var requiredRef = hashSetConfig.RequiredRefs.First();
            Assert.That(requiredRef.Config.Value, Is.EqualTo(33));

            // Verify optional references
            Assert.That(hashSetConfig.OptionalRefs, Has.Count.EqualTo(2));
            var optionalRefs = hashSetConfig.OptionalRefs.ToList();
            var scores = optionalRefs.Select(r => r.Config?.Score).Where(s => s.HasValue).ToList();
            Assert.That(scores, Has.Count.EqualTo(2));
            Assert.That(scores.Contains(1.1f), Is.True);
            Assert.That(scores.Contains(2.2f), Is.True);
        }

        [Test]
        public void ConfigReference_HashSetReferencesWithDuplicates_ShouldHandleDuplicates()
        {
            // Arrange - Create a config with duplicate references in the hashset
            var config1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "hashset-duplicate-target",
                "Name": "Duplicate Target Config",
                "Value": 42
            }
            """;

            var hashSetConfigJson = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+HashSetReferenceConfig",
                "Id": "hashset-duplicate-config",
                "Name": "HashSet With Duplicates",
                "RequiredRefs": ["hashset-duplicate-target", "hashset-duplicate-target"],
                "OptionalRefs": ["non-existent-optional", ""]
            }
            """;

            _fileSource.AddFile("hashset-duplicate-target.json", config1Json);
            _fileSource.AddFile("hashset-duplicate-config.json", hashSetConfigJson);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result = _configDatabase.TryGetConfig("hashset-duplicate-config", typeof(HashSetReferenceConfig), out var config);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(config, Is.InstanceOf<HashSetReferenceConfig>());

            var hashSetConfig = (HashSetReferenceConfig)config;
            Assert.That(hashSetConfig.Name, Is.EqualTo("HashSet With Duplicates"));

            // HashSet should deduplicate based on reference equality (same Id)
            Assert.That(hashSetConfig.RequiredRefs, Has.Count.EqualTo(1));
            Assert.That(hashSetConfig.OptionalRefs, Has.Count.EqualTo(2)); // One valid, one empty

            // Verify the references point to the same config
            var requiredRefsList = hashSetConfig.RequiredRefs.ToList();
            Assert.That(requiredRefsList[0].Config.Value, Is.EqualTo(42));

            var optionalRefsList = hashSetConfig.OptionalRefs.ToList();
            // One optional ref is empty string (null), one is non-existent (should throw)
            var emptyRef = optionalRefsList.First(r => r.Id == "");
            var nonExistentRef = optionalRefsList.First(r => r.Id == "non-existent-optional");

            Assert.That(emptyRef.Config, Is.Null); // Empty string returns null
            Assert.Throws<Exception>(() => { var config = nonExistentRef.Config; }); // Non-existent throws
        }

        [Test]
        public void ConfigReference_HashSetReferencesEquality_ShouldWorkCorrectly()
        {
            // Arrange - Create two configs with same references
            var config1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+TestConfig",
                "Id": "hashset-equality-target",
                "Name": "Equality Target Config",
                "Value": 100
            }
            """;

            var hashSetConfig1Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+HashSetReferenceConfig",
                "Id": "hashset-equality-1",
                "Name": "HashSet Config 1",
                "RequiredRefs": ["hashset-equality-target"],
                "OptionalRefs": []
            }
            """;

            var hashSetConfig2Json = """
            {
                "$type": "Alco.Engine.Test.TestConfigDatabase+HashSetReferenceConfig",
                "Id": "hashset-equality-2",
                "Name": "HashSet Config 2",
                "RequiredRefs": ["hashset-equality-target"],
                "OptionalRefs": []
            }
            """;

            _fileSource.AddFile("hashset-equality-target.json", config1Json);
            _fileSource.AddFile("hashset-equality-1.json", hashSetConfig1Json);
            _fileSource.AddFile("hashset-equality-2.json", hashSetConfig2Json);
            _configDatabase.AddFileSource(_fileSource);

            // Act
            var result1 = _configDatabase.TryGetConfig("hashset-equality-1", typeof(HashSetReferenceConfig), out var config1);
            var result2 = _configDatabase.TryGetConfig("hashset-equality-2", typeof(HashSetReferenceConfig), out var config2);

            // Assert
            Assert.That(result1, Is.True);
            Assert.That(result2, Is.True);

            var hashSetConfig1 = (HashSetReferenceConfig)config1;
            var hashSetConfig2 = (HashSetReferenceConfig)config2;

            // Verify the references are equal (same Id) but point to different reference objects
            var ref1 = hashSetConfig1.RequiredRefs.First();
            var ref2 = hashSetConfig2.RequiredRefs.First();

            Assert.That(ref1.Id, Is.EqualTo(ref2.Id));
            Assert.That(ref1, Is.EqualTo(ref2)); // Equality based on Id
            Assert.That(ref1, Is.Not.SameAs(ref2)); // But different instances

            // Both should resolve to the same config instance
            Assert.That(ref1.Config, Is.SameAs(ref2.Config));
            Assert.That(ref1.Config.Value, Is.EqualTo(100));
        }

        #endregion
    }
}