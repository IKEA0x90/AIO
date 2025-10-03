using System;
using System.Collections.Generic;

namespace AIO.Common {
    public struct Sample {
        public double Timestamp;
        public float[] Features;
        public int Label;
    }

    public class ModelState {
        public string AccessoryID;
        public LRModel Model;
        public Queue<Sample> PendingSamples = new();
        public List<Sample> TrainBuffer = new();
        public List<Sample> ValBuffer = new();
        public int PositivesCount;
        public int TotalSamples;
        public int Stage = 1;
        public int LastSeenPlayer = -1;
        public float PSmoothed = 0f;
        public int ConsecutiveHigh = 0;
        public int CooldownTicks = 0;
        private int _sgdSteps;
        private readonly int _featureDim;
        private readonly Random _rng = new();

        public ModelState(string id, int featureDim) {
            AccessoryID = id;
            _featureDim = featureDim;
            Model = new LRModel(featureDim);
        }

        public void EnqueueSample(Sample s) => PendingSamples.Enqueue(s);

        public void AddLabeledSample(Sample s) {
            TotalSamples++;
            if (s.Label == 1) PositivesCount++;
            TrainBuffer.Add(s);
        }

        public void PerformSGDStep(float lr0, float decay, float lambda, int batchSize) {
            if (TrainBuffer.Count == 0) return;
            var positives = new List<Sample>();
            foreach (var s in TrainBuffer) if (s.Label == 1) positives.Add(s);
            float positiveTargetFrac = 0.5f;
            int targetPositives = (int)(batchSize * positiveTargetFrac);
            var batch = new List<Sample>(batchSize);
            for (int i = 0; i < targetPositives; i++) { if (positives.Count == 0) break; batch.Add(positives[_rng.Next(positives.Count)]); }
            while (batch.Count < batchSize) batch.Add(TrainBuffer[_rng.Next(TrainBuffer.Count)]);
            float lr = lr0 / (1f + decay * _sgdSteps); _sgdSteps++;
            float[][] X = new float[batch.Count][]; float[] y = new float[batch.Count];
            for (int i = 0; i < batch.Count; i++) { X[i] = batch[i].Features; y[i] = batch[i].Label; }
            Model.UpdateSGD(X, y, lr, lambda);
        }
    }
}