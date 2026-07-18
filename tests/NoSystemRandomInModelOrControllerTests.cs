// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace FourExHex.Tests;

// Guardrail: no System.Random in FourExHex.Model or
// FourExHex.Controller. Its bounded overloads (Next(max) /
// Next(min,max)) hide a BCL double multiply, so any draw through them
// puts floating-point on the deterministic game-state path — invisible
// to NoFloatsInModelOrControllerTests, which scans only our own
// signatures. All game-state randomness goes through DeterministicRng
// (integer-only, bit-exact, pinned by DeterministicRngTests).
//
// Same mechanism as the no-floats scan: fields, properties,
// parameters, return types, and method-body locals in both assemblies.
//
// Allowlisted: SeedFormat.NextSeed(Random) — the off-path fresh
// master-seed fallback that scripts/ feeds Random.Shared into. It only
// picks the master seed; it never runs on the seeded/replay path.
public class NoSystemRandomInModelOrControllerTests
{
    [Fact]
    public void ModelAssembly_HasNoSystemRandomAnywhere()
    {
        AssertNoSystemRandom(typeof(GameState).Assembly, "FourExHex.Model");
    }

    [Fact]
    public void ControllerAssembly_HasNoSystemRandomAnywhere()
    {
        AssertNoSystemRandom(typeof(GameController).Assembly, "FourExHex.Controller");
    }

    private static void AssertNoSystemRandom(Assembly assembly, string label)
    {
        var offenders = new List<string>();

        foreach (Type type in assembly.GetTypes())
        {
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
                if (IsSystemRandom(field.FieldType))
                {
                    offenders.Add(
                        $"  field   {typeLabel}.{field.Name} : {Pretty(field.FieldType)}");
                }
            }

            foreach (PropertyInfo prop in type.GetProperties(everything))
            {
                if (IsSystemRandom(prop.PropertyType))
                {
                    offenders.Add(
                        $"  prop    {typeLabel}.{prop.Name} : {Pretty(prop.PropertyType)}");
                }
            }

            foreach (MethodInfo method in type.GetMethods(everything))
            {
                ScanMethod(method, type, typeLabel, offenders);
            }

            foreach (ConstructorInfo ctor in type.GetConstructors(everything))
            {
                ScanMethod(ctor, type, typeLabel, offenders);
            }
        }

        if (offenders.Count > 0)
        {
            offenders.Sort(StringComparer.Ordinal);
            string message =
                $"{label}: {offenders.Count} System.Random usage(s) found " +
                $"(see issue #59 — game-state randomness goes through DeterministicRng):\n" +
                string.Join("\n", offenders);
            Assert.Fail(message);
        }
    }

    /// <summary>The one sanctioned System.Random touchpoint: the
    /// off-path fresh master-seed fallback.</summary>
    private static bool IsAllowlisted(Type declaringType, MethodBase method) =>
        declaringType.Name == nameof(SeedFormat) &&
        method.Name == nameof(SeedFormat.NextSeed);

    private static void ScanMethod(
        MethodBase method, Type declaringType, string typeLabel, List<string> offenders)
    {
        if (IsCompilerGenerated(method)) return;
        if (IsAllowlisted(declaringType, method)) return;

        string memberLabel = $"{typeLabel}.{method.Name}";

        if (method is MethodInfo mi && IsSystemRandom(mi.ReturnType))
        {
            offenders.Add($"  return  {memberLabel} : {Pretty(mi.ReturnType)}");
        }

        foreach (ParameterInfo p in method.GetParameters())
        {
            if (IsSystemRandom(p.ParameterType))
            {
                offenders.Add(
                    $"  param   {memberLabel}({p.Name}) : {Pretty(p.ParameterType)}");
            }
        }

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
                if (IsSystemRandom(local.LocalType))
                {
                    offenders.Add(
                        $"  local   {memberLabel}[#{local.LocalIndex}] : {Pretty(local.LocalType)}");
                }
            }
        }
    }

    private static bool IsSystemRandom(Type? type)
    {
        if (type == null) return false;

        if (type.IsByRef || type.IsPointer)
        {
            type = type.GetElementType();
            if (type == null) return false;
        }

        if (type == typeof(Random)) return true;

        if (type.IsArray)
        {
            return IsSystemRandom(type.GetElementType());
        }

        // Catch generic args: Func<..., Random, ...>, List<Random>, etc.
        if (type.IsGenericType)
        {
            foreach (Type arg in type.GetGenericArguments())
            {
                if (IsSystemRandom(arg)) return true;
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
