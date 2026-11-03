/*
 *  WattyPatty - Story metadata extractor for Wattpad - ConsoleSynchronization.cs
 *  Copyright (C) 2026 Dottik
 *
 *  This program is free software: you can redistribute it and/or modify
 *  it under the terms of the GNU Affero General Public License as published
 *  by the Free Software Foundation, either version 3 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU Affero General Public License for more details.
 *
 *  You should have received a copy of the GNU Affero General Public License
 *  along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

namespace WattyPatty;

/// <summary>
///     Single gate for console output. Chapters are downloaded concurrently and network diagnostics are
///     raised from those workers, so writes must be serialized to avoid interleaved lines.
/// </summary>
internal static class ConsoleSynchronization {
    public static readonly object Gate = new();
}
