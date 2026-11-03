/*
 *  WattyPatty - Story metadata extractor for Wattpad - GlobalRequestThrottle.cs
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
///     Process-wide cooldown gate shared by every request, so a single rate-limit response pauses all
///     concurrent downloads.
/// </summary>
internal sealed class GlobalRequestThrottle {
    private static readonly TimeSpan MaxPenalty = TimeSpan.FromSeconds(15);

    private readonly object m_gate = new();
    private bool m_abandoned;
    private DateTimeOffset m_blockedUntil = DateTimeOffset.MinValue;
    private int m_penaltyLevel;

    /// <summary>True once the client has given up waiting for the rate limiter to clear.</summary>
    public bool IsAbandoned {
        get {
            lock (m_gate)
                return m_abandoned;
        }
    }

    /// <summary>Current cooldown, for diagnostics.</summary>
    public TimeSpan CurrentCooldown {
        get {
            lock (m_gate) {
                var remaining = m_blockedUntil - DateTimeOffset.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }
    }

    /// <summary>Block the caller until the current cooldown expires.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken) {
        while (true) {
            TimeSpan remaining;

            lock (m_gate)
                remaining = m_abandoned ? TimeSpan.Zero : m_blockedUntil - DateTimeOffset.UtcNow;

            if (remaining <= TimeSpan.Zero)
                return;

            await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Called whenever the server answered 429, extending the shared cooldown.</summary>
    public void ReportThrottled() {
        lock (m_gate) {
            if (m_abandoned)
                return;

            m_penaltyLevel = Math.Min(m_penaltyLevel + 1, 5);

            var penalty = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, m_penaltyLevel - 1), MaxPenalty.TotalSeconds));
            var blockedUntil = DateTimeOffset.UtcNow + penalty;

            if (blockedUntil > m_blockedUntil)
                m_blockedUntil = blockedUntil;
        }
    }

    /// <summary>
    ///     Called after a successful request. The penalty is only cleared once the cooldown has fully
    ///     elapsed, so a single lucky request cannot immediately reopen the floodgates.
    /// </summary>
    public void ReportSuccess() {
        lock (m_gate) {
            if (!m_abandoned && m_penaltyLevel > 0 && DateTimeOffset.UtcNow >= m_blockedUntil)
                m_penaltyLevel = 0;
        }
    }

    /// <summary>
    ///     Stop waiting for the rate limiter. Every following request fails immediately, which lets the
    ///     scrape finish with whatever was already fetched.
    /// </summary>
    public void ReportForfeited() {
        lock (m_gate)
            m_abandoned = true;
    }
}
