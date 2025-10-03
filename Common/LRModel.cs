using System;
using Terraria.ModLoader.IO;

namespace AIO.Common {
    /// <summary>
    /// Simple logistic regression model with SGD updates.
    /// </summary>
    public class LRModel {
        public float[] W;
        public float B;
        private readonly int _dim;

        public LRModel(int dim) {
            _dim = dim;
            W = new float[dim];
            var rng = new Random();
            for (int i = 0; i < dim; i++) {
                W[i] = (float)(rng.NextDouble() * 0.02 - 0.01); // small init
            }
            B = 0;
        }

        public LRModel(float[] w, float b) {
            W = w;
            B = b;
            _dim = w.Length;
        }

        public float PredictProb(float[] x) {
            double z = B;
            for (int i = 0; i < W.Length; i++)
                z += W[i] * x[i];
            return Sigmoid((float)z);
        }

        private float Sigmoid(float z) => 1f / (1f + (float)Math.Exp(-z));

        public void UpdateSGD(float[][] X, float[] y, float lr, float lambda) {
            int m = X.Length;
            float[] gradW = new float[W.Length];
            float gradB = 0f;

            for (int i = 0; i < m; i++) {
                float p = PredictProb(X[i]);
                float diff = p - y[i];
                for (int j = 0; j < W.Length; j++) {
                    gradW[j] += diff * X[i][j];
                }
                gradB += diff;
            }
            for (int j = 0; j < W.Length; j++) {
                gradW[j] = gradW[j] / m + lambda * W[j];
                W[j] -= lr * gradW[j];
            }
            B -= lr * (gradB / m);
        }

        public TagCompound Serialize() {
            return new TagCompound {
                ["ModelW"] = W,
                ["ModelB"] = B
            };
        }

        public static LRModel Deserialize(TagCompound tag) {
            var w = tag.Get<float[]>("ModelW");
            var b = tag.GetFloat("ModelB");
            return new LRModel(w, b);
        }
    }
}