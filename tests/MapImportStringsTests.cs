// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The Model-side <see cref="MapImportError"/> enum carries no string
/// keys (Model can't reference the controller's StringKeys); this pins
/// the controller-side mapping so every error the validator can produce
/// has a localized message, routed through a real StringKeys constant
/// (and therefore covered by the key↔en.json parity test).
/// </summary>
public class MapImportStringsTests
{
    [Fact]
    public void KeyFor_MapsEveryErrorToAStringKeysConstant()
    {
        HashSet<string> knownKeys = typeof(StringKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        foreach (MapImportError error in Enum.GetValues<MapImportError>())
        {
            string key = MapImportStrings.KeyFor(error);
            Assert.Contains(key, knownKeys);
        }
    }

    [Fact]
    public void KeyFor_DistinguishesTooNewFromMalformed()
    {
        // "Needs a newer app" and "not a valid map" demand different user
        // messages — the mapping must not collapse them.
        Assert.NotEqual(
            MapImportStrings.KeyFor(MapImportError.TooNew),
            MapImportStrings.KeyFor(MapImportError.Malformed));
    }
}
