using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.Build.Profile;

public static class UnityEditorInternalApi
{
    private const BindingFlags StaticMethodBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static string GetStreamingAssetsBundleManifestPath()
    {
        return InvokeStatic<string>(
            typeof(BuildPipeline).Assembly,
            "UnityEditor.PostprocessBuildPlayer",
            "GetStreamingAssetsBundleManifestPath");
    }

    public static BuildOptions GetBuildOptions(
        BuildTarget buildTarget,
        BuildTargetGroup buildTargetGroup,
        string buildLocation,
        BuildOptions options = BuildOptions.None)
    {
        return InvokeStatic<BuildOptions>(
            typeof(BuildProfile).Assembly,
            "UnityEditor.Build.Profile.BuildProfileModuleUtil",
            "GetBuildOptions",
            new[] { typeof(BuildTarget), typeof(BuildTargetGroup), typeof(string), typeof(BuildOptions) },
            new object[] { buildTarget, buildTargetGroup, buildLocation, options }
        );
    }

    private static TResult InvokeStatic<TResult>(Assembly assembly, string typeName, string methodName)
    {
        return InvokeStatic<TResult>(
            assembly,
            typeName,
            methodName,
            Type.EmptyTypes,
            Array.Empty<object>());
    }

    private static TResult InvokeStatic<TResult>(
        Assembly assembly,
        string typeName,
        string methodName,
        Type[] parameterTypes,
        object[] arguments)
    {
        var method = FindStaticMethod(assembly, typeName, methodName, parameterTypes);

        try
        {
            var result = method.Invoke(null, arguments);
            if (result == null)
            {
                if (!typeof(TResult).IsValueType || Nullable.GetUnderlyingType(typeof(TResult)) != null)
                {
                    return default;
                }

                throw new InvalidOperationException($"Unity internal method '{typeName}.{methodName}' returned null.");
            }

            if (result is TResult typedResult)
            {
                return typedResult;
            }

            throw new InvalidOperationException(
                $"Unity internal method '{typeName}.{methodName}' returned '{result.GetType().FullName}' instead of '{typeof(TResult).FullName}'.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static MethodInfo FindStaticMethod(Assembly assembly, string typeName, string methodName, Type[] parameterTypes)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        var type = assembly.GetType(typeName);
        if (type == null)
        {
            throw new InvalidOperationException($"Could not find Unity internal type '{typeName}' in assembly '{assembly.GetName().Name}'.");
        }

        var method = type.GetMethod(
            methodName,
            StaticMethodBindingFlags,
            null,
            parameterTypes,
            null);

        if (method == null)
        {
            throw new InvalidOperationException($"Could not find Unity internal method '{typeName}.{methodName}' in assembly '{assembly.GetName().Name}'.");
        }

        return method;
    }
}
