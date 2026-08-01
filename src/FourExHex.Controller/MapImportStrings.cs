// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Maps the Model-side <see cref="MapImportError"/> categories to the
/// localized message keys the import UI shows. Lives in the controller
/// library because Model cannot reference <see cref="StringKeys"/>.
/// </summary>
public static class MapImportStrings
{
    public static string KeyFor(MapImportError error) => error switch
    {
        MapImportError.Malformed => StringKeys.ImportErrorMalformed,
        MapImportError.TooNew => StringKeys.ImportErrorVersion,
        MapImportError.NotStartingMap => StringKeys.ImportErrorNotStartingMap,
        MapImportError.TooLarge => StringKeys.ImportErrorTooLarge,
        MapImportError.Invalid => StringKeys.ImportErrorInvalid,
        _ => throw new ArgumentOutOfRangeException(nameof(error), error, null),
    };
}
