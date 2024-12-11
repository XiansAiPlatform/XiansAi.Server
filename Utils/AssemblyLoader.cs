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
            return LoadTypes(workflowAssemblies, typeof(IWorkflow<,>));
        }

        public IEnumerable<Type> GetActivityTypes()
        {
            var activityAssemblies = configuration.GetSection("Temporal:ActivityAssemblies").Get<string[]>();
            return LoadTypes(activityAssemblies, typeof(IActivity));
        }
        public IEnumerable<Type> LoadTypes(string[]? assemblyNames, Type interfaceType)
        {
            var assemblyLoadContext = new AssemblyLoadContext("FlowmaxerLoadContext", isCollectible: true);
            if (assemblyNames == null) return Enumerable.Empty<Type>();

            return assemblyNames
                .SelectMany(assemblyName => 
                {
                    // Load the assembly using the instance
                    var absolutePath = Path.GetFullPath(assemblyName);
                    var assembly = assemblyLoadContext.LoadFromAssemblyPath(absolutePath);
                    return assembly.GetTypes();
                })
                .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
        }
    }
}
