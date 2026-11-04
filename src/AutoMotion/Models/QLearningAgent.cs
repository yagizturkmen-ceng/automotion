using System;
using System.Collections.Generic;

namespace AutoMotion.Models
{
    /// <summary>
    /// Bağımsız (harici ML kütüphanesi/ağ erişimi gerektirmeyen), tablo tabanlı Q-learning ajanı.
    /// Durum ve eylemler tam sayı ID'leriyle temsil edilir - çağıran taraf (bkz. Vehicle
    /// UpdateLearningNavigation) durumu kodlar ve ödülü hesaplar. Bellman güncellemesi klasik
    /// formülüyle yapılır: Q(s,a) += α * (ödül + γ * max_a' Q(s',a') - Q(s,a)).
    /// </summary>
    public class QLearningAgent
    {
        private readonly Dictionary<(int state, int action), double> qTable = new();
        private readonly Random random;

        public double LearningRate { get; set; } = 0.2;
        public double DiscountFactor { get; set; } = 0.9;
        public double ExplorationRate { get; set; } = 0.15;
        public int ActionCount { get; }

        public QLearningAgent(int actionCount, Random random = null)
        {
            if (actionCount <= 0) throw new ArgumentOutOfRangeException(nameof(actionCount));
            ActionCount = actionCount;
            this.random = random ?? Random.Shared;
        }

        private double Q(int state, int action) => qTable.TryGetValue((state, action), out var v) ? v : 0.0;

        /// <summary>Epsilon-greedy: çoğunlukla en iyi bilinen eylemi seçer, ExplorationRate ihtimalle
        /// rastgele bir eylem dener (yeni durumları keşfetmek için).</summary>
        public int ChooseAction(int state)
        {
            if (random.NextDouble() < ExplorationRate)
                return random.Next(ActionCount);

            int bestAction = 0;
            double bestValue = double.NegativeInfinity;
            for (int a = 0; a < ActionCount; a++)
            {
                double v = Q(state, a);
                if (v > bestValue) { bestValue = v; bestAction = a; }
            }
            return bestAction;
        }

        public void Learn(int state, int action, double reward, int nextState)
        {
            double maxNext = double.NegativeInfinity;
            for (int a = 0; a < ActionCount; a++) maxNext = Math.Max(maxNext, Q(nextState, a));
            if (double.IsNegativeInfinity(maxNext)) maxNext = 0;

            double oldQ = Q(state, action);
            double newQ = oldQ + LearningRate * (reward + DiscountFactor * maxNext - oldQ);
            qTable[(state, action)] = newQ;
        }

        /// <summary>Test/gözlem amaçlı: şu ana kadar öğrenilen (durum, eylem) çifti sayısı.</summary>
        public int KnownStateActionCount => qTable.Count;

        public double GetQ(int state, int action) => Q(state, action);
    }
}
