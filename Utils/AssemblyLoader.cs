using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using Flowmaxer.Common;

namespace Flowmaxer.Utils {
    public class AssemblyLoader
    {
        private readonly IConfiguration configuration;

        public AssemblyLoader(IConfiguration configuration)
        {
            this.configuration = configuration;
        }
        public IEnumerable<Type> GetWorkflowTypes()
        {
            var workflowAssemblies = configuration.GetSection("Temporal:WorkflowAssemblies").Get<string[]>();
            var types = LoadTypes(workflowAssemblies, typeof(IWorkflow<,>));
            Console.WriteLine($"Loaded {types.Count()} workflow types.");
            return types;
        }

        public IEnumerable<Type> GetActivityTypes()
        {
            var activityAssemblies = configuration.GetSection("Temporal:ActivityAssemblies").Get<string[]>();
            var types = LoadTypes(activityAssemblies, typeof(IActivity));
            Console.WriteLine($"Loaded {types.Count()} activity types.");
            return types;
        }
        public IEnumerable<Type> LoadTypes(string[]? assemblyNames, Type interfaceType)
        {
            var assemblyLoadContext = new AssemblyLoadContext("FlowmaxerLoadContext", isCollectible: true);
            if (assemblyNames == null) 
            {
                Console.WriteLine("No assembly names provided.");
                return Enumerable.Empty<Type>();
            }

            var types = assemblyNames
                .SelectMany(assemblyName => 
                {
                    try
                    {
                        // Load the assembly using the instance
                        var absolutePath = Path.GetFullPath(assemblyName);
                        Console.WriteLine($"Loading assembly from path: {absolutePath}");
                        var assembly = assemblyLoadContext.LoadFromAssemblyPath(absolutePath);
                        var types = assembly.GetTypes();
                        Console.WriteLine($"Loaded {types.Length} types from assembly: {assembly.FullName}");
                        return types;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading assembly {assemblyName}: {ex.Message}");
                        return Enumerable.Empty<Type>();
                    }
                })
                .Where(t => 
                {
                    bool hasMatchingProperties = t.GetInterfaces().Any(i => 
                        i.Name == interfaceType.Name &&
                        i.Namespace == interfaceType.Namespace);

                    bool isConcrete = !t.IsInterface && !t.IsAbstract;

                    if (hasMatchingProperties && isConcrete)
                    {
                        Console.WriteLine($"Type {t.FullName} has matching properties with the interface.");
                    }
                    return hasMatchingProperties && isConcrete;
                });

            return types;
        }
    }
}
