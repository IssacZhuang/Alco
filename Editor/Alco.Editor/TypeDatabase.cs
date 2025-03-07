using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Alco.IO;

namespace Alco.Editor;

public class TypeDatabase : IDisposable
{
    private readonly List<Type> _configTypes = new();
    private bool _isConfigTypesDirty = false;


    public IReadOnlyList<Type> ConfigTypes
    {
        get
        {
            if (_isConfigTypesDirty)
            {
                FetchAllConfigTypes();
            }
            return _configTypes;
        }
    }

    public TypeDatabase()
    {
        FetchAllConfigTypes();
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
    }




    private void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        _isConfigTypesDirty = true;
    }



    private void FetchAllConfigTypes()
    {
        _configTypes.Clear();
        _configTypes.AddRange(
            Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsSubclassOf(typeof(BaseConfig)) && !t.IsAbstract)
        );
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
    }
}

