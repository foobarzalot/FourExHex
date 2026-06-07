using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace FourExHex.Tests;

// Guardrail for issue #20: zero floating-point math in
// FourExHex.Model and FourExHex.Controller. float / double are not
// deterministic across platforms, compilers, and JIT levels — any
// floating-point in the game-state code path is a desync time bomb
// for networked multiplayer (#6).
//
// This test scans the Model and Controller assemblies via
// System.Reflection and fails the build if any field, property,
// parameter, return type, or method-body local has a Single (float)
// or Double (double) type.
//
// Caveat: this catches type signatures + locals. A float that lives
// only on the IL eval stack between a `call` and an immediate use
// could slip through. In practice you can't compute with floats
// without storing one somewhere, so this is a ~99% net. If a slip
// ever happens, the test can be hardened by walking IL instructions
// via Mono.Cecil (look for ldc.r4 / ldc.r8 / conv.r4 / conv.r8
// opcodes).
//
// View-math that legitimately needs floats (DPI, layout, geometry)
// lives in the FourExHex.ViewMath project, which is intentionally
// not scanned. See ARCHITECTURE.md for the layering.
public class NoFloatsInModelOrControllerTests
{
    [Fact]
    public void ModelAssembly_HasNoFloatOrDoubleAnywhere()
    {
        // Anchor: any non-trivial type from Model.
        Assembly modelAssembly = typeof(GameState).Assembly;
        AssertNoFloatingPoint(modelAssembly, "FourExHex.Model");
    }

    [Fact]
    public void ControllerAssembly_HasNoFloatOrDoubleAnywhere()
    {
        // Anchor: any non-trivial type from Controller.
        Assembly controllerAssembly = typeof(GameController).Assembly;
        AssertNoFloatingPoint(controllerAssembly, "FourExHex.Controller");
    }

    private static void AssertNoFloatingPoint(Assembly assembly, string label)
    {
        var offenders = new List<string>();

        foreach (Type type in assembly.GetTypes())
        {
            // Skip compiler-generated closure / iterator types — they
            // mirror their parent's signatures and would double-report.
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                continue;
            }

            string typeLabel = type.FullName ?? type.Name;

            const BindingFlags everything =
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly;

            foreach (FieldInfo field in type.GetFields(everything))
            {
                if (IsCompilerGenerated(field)) continue;
                if (IsFloating(field.FieldType))
                {
                    offenders.Add(
                        $"  field   {typeLabel}.{field.Name} : {Pretty(field.FieldType)}");
                }
            }

            foreach (PropertyInfo prop in type.GetProperties(everything))
            {
                if (IsFloating(prop.PropertyType))
                {
                    offenders.Add(
                        $"  prop    {typeLabel}.{prop.Name} : {Pretty(prop.PropertyType)}");
                }
            }

            foreach (MethodInfo method in type.GetMethods(everything))
            {
                ScanMethod(method, typeLabel, offenders);
            }

            foreach (ConstructorInfo ctor in type.GetConstructors(everything))
            {
                ScanMethod(ctor, typeLabel, offenders);
            }
        }

        if (offenders.Count > 0)
        {
            offenders.Sort(StringComparer.Ordinal);
            string message =
                $"{label}: {offenders.Count} floating-point usage(s) found " +
                $"(see issue #20 — Model/Controller must be integer-only):\n" +
                string.Join("\n", offenders);
            Assert.Fail(message);
        }
    }

    private static void ScanMethod(MethodBase method, string typeLabel, List<string> offenders)
    {
        if (IsCompilerGenerated(method)) return;

        string memberLabel = $"{typeLabel}.{method.Name}";

        if (method is MethodInfo mi && IsFloating(mi.ReturnType))
        {
            offenders.Add($"  return  {memberLabel} : {Pretty(mi.ReturnType)}");
        }

        foreach (ParameterInfo p in method.GetParameters())
        {
            if (IsFloating(p.ParameterType))
            {
                offenders.Add(
                    $"  param   {memberLabel}({p.Name}) : {Pretty(p.ParameterType)}");
            }
        }

        // Method-body locals catch internal accumulators like
        // `double total = 0.0;` that don't show on the public surface.
        // Abstract / extern / interface methods have no body.
        MethodBody? body = null;
        try
        {
            body = method.GetMethodBody();
        }
        catch (Exception)
        {
            // GetMethodBody can throw for some runtime-implemented
            // members; skip those.
        }

        if (body != null)
        {
            foreach (LocalVariableInfo local in body.LocalVariables)
            {
                if (IsFloating(local.LocalType))
                {
                    offenders.Add(
                        $"  local   {memberLabel}[#{local.LocalIndex}] : {Pretty(local.LocalType)}");
                }
            }
        }
    }

    private static bool IsFloating(Type? type)
    {
        if (type == null) return false;

        // Unwrap by-ref (out / ref parameters) and pointer wrappers.
        if (type.IsByRef || type.IsPointer)
        {
            type = type.GetElementType();
            if (type == null) return false;
        }

        if (type == typeof(float) || type == typeof(double))
        {
            return true;
        }

        // Catch nullable: float? / double?.
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            if (underlying == typeof(float) || underlying == typeof(double))
            {
                return true;
            }
        }

        // Catch arrays: float[], double[][], etc.
        if (type.IsArray)
        {
            return IsFloating(type.GetElementType());
        }

        // Catch generic args: List<float>, Dictionary<int, double>,
        // Func<float>, ValueTuple<float, float>, etc.
        if (type.IsGenericType)
        {
            foreach (Type arg in type.GetGenericArguments())
            {
                if (IsFloating(arg)) return true;
            }
        }

        return false;
    }

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static string Pretty(Type type)
    {
        if (type.IsGenericType)
        {
            string name = type.Name;
            int tick = name.IndexOf('`');
            if (tick >= 0) name = name.Substring(0, tick);
            string args = string.Join(", ", type.GetGenericArguments().Select(Pretty));
            return $"{name}<{args}>";
        }
        return type.Name;
    }
}
