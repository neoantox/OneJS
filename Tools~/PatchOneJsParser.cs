using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;

// Run with Pipeline `script run`; this file is not imported into the game.
public static class PatchOneJsParser
{
    const string Package = "Packages/com.singtaa.onejs";
    const string OriginalHash = "3072e203644d4df4fd5f6e495c46179afffe3bb6e66e1f1ebb8de80b17f35dc9";

    public static object Run()
    {
        var oldPath = Package + "/Runtime/Styling/ExCSS.Unity.dll";
        var newPath = Package + "/Runtime/Styling/OneJS.ExCSS.dll";
        var asmdefPath = Package + "/Runtime/OneJS.Runtime.asmdef";
        if (!Directory.Exists(Package)) throw new InvalidOperationException("Embed com.singtaa.onejs before patching it.");
        if (!File.Exists(oldPath) && File.Exists(newPath))
        {
            using var existing = (IDisposable)Read(newPath);
            if ((string)Get(Get(existing, "Name"), "Name") != "OneJS.ExCSS" || !File.ReadAllText(asmdefPath).Contains("\"OneJS.ExCSS.dll\""))
                throw new InvalidOperationException("The embedded parser patch is incomplete.");
            return new { alreadyPatched = true, sha256 = Hash(File.ReadAllBytes(newPath)) };
        }
        if (Hash(File.ReadAllBytes(oldPath)) != OriginalHash)
            throw new InvalidOperationException("Unexpected upstream parser bytes. Review the patch against the new OneJS version.");
        var asmdef = File.ReadAllText(asmdefPath);
        if (!asmdef.Contains("\"ExCSS.Unity.dll\"")) throw new InvalidOperationException("Unexpected runtime asmdef.");
        var temp = Path.GetTempFileName();
        try
        {
            using var resolver = (IDisposable)Activator.CreateInstance(Cecil.GetType("Mono.Cecil.DefaultAssemblyResolver"));
            Call(resolver, "AddSearchDirectory", Path.GetDirectoryName(typeof(object).Assembly.Location));
            var facades = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location), "Facades");
            if (Directory.Exists(facades)) Call(resolver, "AddSearchDirectory", facades);
            Call(resolver, "AddSearchDirectory", Path.GetDirectoryName(typeof(UnityEditor.Editor).Assembly.Location));
            var reader = Activator.CreateInstance(Cecil.GetType("Mono.Cecil.ReaderParameters"));
            Set(reader, "InMemory", true);
            Set(reader, "AssemblyResolver", resolver);
            using var assembly = (IDisposable)Read(oldPath, reader);
            var name = Get(assembly, "Name");
            if ((string)Get(name, "Name") != "ExCSS.Unity" || (bool)Get(name, "HasPublicKey"))
                throw new InvalidOperationException("Expected the unsigned upstream ExCSS.Unity assembly.");
            var methods = MethodFingerprint(assembly);
            Set(name, "Name", "OneJS.ExCSS");
            Set(Get(assembly, "MainModule"), "Name", "OneJS.ExCSS.dll");
            Call(assembly, "Write", temp);
            using var patched = (IDisposable)Read(temp, reader);
            if (MethodFingerprint(patched) != methods)
                throw new InvalidOperationException("Parser method bodies changed during the assembly rename.");

            AssetDatabase.StartAssetEditing();
            try
            {
                File.Move(temp, newPath);
                File.Move(oldPath + ".meta", newPath + ".meta"); // Preserve the asset GUID and importer settings.
                File.WriteAllText(asmdefPath, asmdef.Replace("\"ExCSS.Unity.dll\"", "\"OneJS.ExCSS.dll\""));
                File.Delete(oldPath);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            return new { assembly = Get(Get(patched, "Name"), "FullName"), sourceSha256 = OriginalHash,
                patchedSha256 = Hash(File.ReadAllBytes(newPath)), methodFingerprint = methods };
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    // Pipeline references both Unity.Cecil and Mono.Cecil, with identical type names.
    // Reflection binds Mono.Cecil explicitly without introducing an Editor assembly into the game.
    static Assembly Cecil => AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Mono.Cecil");
    static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    static void Set(object value, string name, object setting) => value.GetType().GetProperty(name).SetValue(value, setting);
    static IEnumerable<object> Items(object value) => ((IEnumerable)value).Cast<object>();
    static object Call(object value, string name, params object[] args)
    {
        var method = value.GetType().GetMethods().Single(m => m.Name == name && m.GetParameters().Length == args.Length &&
            m.GetParameters().Select((p, i) => p.ParameterType.IsInstanceOfType(args[i])).All(match => match));
        try { return method.Invoke(value, args); }
        catch (TargetInvocationException error) { throw new InvalidOperationException(error.InnerException.ToString(), error.InnerException); }
    }

    static object Read(string path, object reader = null)
    {
        var type = Cecil.GetType("Mono.Cecil.AssemblyDefinition");
        return reader == null
            ? type.GetMethod("ReadAssembly", new[] { typeof(string) }).Invoke(null, new object[] { path })
            : type.GetMethod("ReadAssembly", new[] { typeof(string), reader.GetType() }).Invoke(null, new[] { path, reader });
    }

    static string MethodFingerprint(object assembly)
    {
        var methods = new List<string>();
        foreach (var type in Items(Call(Get(assembly, "MainModule"), "GetTypes")))
            foreach (var method in Items(Get(type, "Methods")))
                if ((bool)Get(method, "HasBody"))
                    methods.Add((string)Get(method, "FullName") + "\n" + string.Join("\n",
                        Items(Get(Get(method, "Body"), "Instructions")).Select(i => i.ToString())));
        methods.Sort(StringComparer.Ordinal);
        return Hash(Encoding.UTF8.GetBytes(string.Join("\n", methods)));
    }

    static string Hash(byte[] data)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
}
