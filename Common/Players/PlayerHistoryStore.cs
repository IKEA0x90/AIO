using System;
using System.Collections.Generic;

namespace AIO.Common.Players {
    /// <summary>
    /// Maintains a short rolling history per player to resolve sample labels (death within horizon, damage sums).
    /// </summary>
    public class PlayerHistoryStore {
        private class Entry {
            public double Time;
            public int HP;
            public int MaxHP;
            public int PotionDelay;
            public float VelY;
            public bool Hazard;
            public int DamageTakenSinceLast; // Derived
        }

        private readonly Dictionary<int, List<Entry>> _history = new();
        private readonly Dictionary<int, int> _lastHP = new();

        private const double KeepSeconds = 8.0; // >= H(3) + buffer

        public void PushTick(int whoAmI, double time, int hp, int maxHp, int potionDelay, float velY, bool hazard) {
            if (!_history.TryGetValue(whoAmI, out var list)) {
                list = new List<Entry>();
                _history[whoAmI] = list;
            }
            int lastHp = _lastHP.TryGetValue(whoAmI, out var lhp) ? lhp : hp;
            int dmgTaken = Math.Max(0, lastHp - hp);
            list.Add(new Entry {
                Time = time,
                HP = hp,
                MaxHP = maxHp,
                PotionDelay = potionDelay,
                VelY = velY,
                Hazard = hazard,
                DamageTakenSinceLast = dmgTaken
            });
            _lastHP[whoAmI] = hp;

            // Trim old
            double cutoff = time - KeepSeconds;
            while (list.Count > 0 && list[0].Time < cutoff) {
                list.RemoveAt(0);
            }
        }

        public bool DiedBetween(int whoAmI, double start, double end) {
            if (!_history.TryGetValue(whoAmI, out var list))
                return false;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].Time < start)
                    continue;
                if (list[i].Time > end)
                    break;
                if (list[i].HP <= 0)
                    return true;
            }
            return false;
        }

        public float DamageTakenBetween(int whoAmI, double start, double end) {
            if (!_history.TryGetValue(whoAmI, out var list))
                return 0f;
            float sum = 0;
            for (int i = 0; i < list.Count; i++) {
                var e = list[i];
                if (e.Time < start)
                    continue;
                if (e.Time > end)
                    break;
                sum += e.DamageTakenSinceLast;
            }
            return sum;
        }

        public double SecondsSinceLastDamage(int whoAmI, double now) {
            if (!_history.TryGetValue(whoAmI, out var list) || list.Count == 0)
                return 999;
            for (int i = list.Count - 1; i >= 0; i--) {
                if (list[i].DamageTakenSinceLast > 0) {
                    return now - list[i].Time;
                }
            }
            return 999;
        }
    }
}