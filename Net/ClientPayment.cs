using System;
using System.Collections.Generic;
using Il2CppScheduleOne.Money;

namespace RVRepairVan.Net
{
    /// <summary>
    /// Co-op: the player who clicks pays. Cash is per player in Schedule I (<c>MoneyManager.cashBalance</c> reads the
    /// local <c>PlayerInventory.cashInstance</c>), so the host cannot take a client's money - the client charges
    /// itself before it sends the intent, and the host answers with <see cref="RvOp.PaymentSettled"/>: kept when the
    /// host applied the purchase, refunded when it refused (a race, e.g. someone else bought it first).
    ///
    /// The ticket is a random id the client keeps until the answer arrives. Every client sees every broadcast, so
    /// only the one holding that ticket acts on it.
    /// </summary>
    internal static class ClientPayment
    {
        private static readonly Dictionary<int, int> Pending = new Dictionary<int, int>();
        private static readonly System.Random Rng = new System.Random();

        /// <summary>
        /// Take <paramref name="amount"/> from this player's cash and open a ticket. Returns 0 when the player is short
        /// (nothing taken) - the caller then lets the NPC refuse.
        /// </summary>
        internal static int Charge(int amount, string what)
        {
            if (amount < 0) amount = 0;
            if (S1API.Money.Money.GetCashBalance() < amount)
            {
                Core.Log.Msg("[Pay] " + what + " refused - short of " + MoneyManager.FormatAmount(amount) + ".");
                return 0;
            }
            int ticket;
            do { ticket = Rng.Next(1, int.MaxValue); } while (Pending.ContainsKey(ticket));
            if (amount > 0) S1API.Money.Money.ChangeCashBalance(-amount, true, true);
            Pending[ticket] = amount;
            Core.Log.Msg("[Pay] " + what + ": paid " + MoneyManager.FormatAmount(amount) + " (ticket " + ticket + ").");
            return ticket;
        }

        /// <summary>Host answer for a ticket. Ignored unless this client opened it.</summary>
        internal static void Settle(int ticket, bool refund)
        {
            if (!Pending.TryGetValue(ticket, out int amount)) return;   // another player's ticket
            Pending.Remove(ticket);
            if (refund)
            {
                if (amount > 0) S1API.Money.Money.ChangeCashBalance(amount, true, true);
                Core.Log.Warning("[Pay] host refused ticket " + ticket + " - refunded " + MoneyManager.FormatAmount(amount) + ".");
            }
            else Core.Log.Msg("[Pay] host confirmed ticket " + ticket + ".");
        }

        /// <summary>
        /// Scene change: any ticket still open never got its answer (host left mid-purchase). The money stays spent,
        /// which is logged so it is not mistaken for a silent loss.
        /// </summary>
        internal static void Reset()
        {
            if (Pending.Count > 0)
                Core.Log.Warning("[Pay] " + Pending.Count + " payment(s) never answered by the host before the scene changed.");
            Pending.Clear();
        }
    }
}
