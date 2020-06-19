﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.ML.Data;
using Microsoft.ML.TestFramework;
using Microsoft.ML.TimeSeries;
using Microsoft.ML.Transforms.TimeSeries;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ML.Tests
{
    public sealed class TimeSeriesDirectApi : BaseTestClass
    {
        public TimeSeriesDirectApi(ITestOutputHelper output) : base(output)
        {
        }

        private sealed class Prediction
        {
#pragma warning disable CS0649
            [VectorType(4)]
            public double[] Change;
#pragma warning restore CS0649
        }

        private sealed class ForecastPrediction
        {
#pragma warning disable CS0649
            [VectorType(4)]
            public float[] Forecast;
            [VectorType(4)]
            public float[] MinCnf;
            [VectorType(4)]
            public float[] MaxCnf;
#pragma warning restore CS0649
        }

        public class Prediction1
        {
            public float Random;
        }

        private sealed class Data
        {
            public string Text;
            public float Random;
            public float Value;

            public Data(float value)
            {
                Text = "random123value";
                Random = -1;
                Value = value;
            }
        }

        class ForecastResult
        {
#pragma warning disable CS0649
            public float Forecast;
#pragma warning restore CS0649
        }

        class ForecastResultArray
        {
#pragma warning disable CS0649
            public float[] Forecast;
            public float[] ConfidenceLowerBound;
            public float[] ConfidenceUpperBound;
#pragma warning restore CS0649
        }

        private sealed class TimeSeriesData
        {
            public float Value;

            public TimeSeriesData(float value)
            {
                Value = value;
            }
        }

        private sealed class TimeSeriesDataDouble
        {
            [LoadColumn(0)]
            public double Value { get; set; }
        }

        private sealed class SrCnnAnomalyDetection
        {
            [VectorType]
            public double[] Prediction { get; set; }
        }

        private static Object _rootCauseAggSymbol = "##SUM##";


        [Fact]
        public void ChangeDetection()
        {
            var env = new MLContext(1);
            const int size = 10;
            List<Data> data = new List<Data>(size);
            var dataView = env.Data.LoadFromEnumerable(data);
            for (int i = 0; i < size / 2; i++)
                data.Add(new Data(5));

            for (int i = 0; i < size / 2; i++)
                data.Add(new Data((float)(5 + i * 1.1)));

            var args = new IidChangePointDetector.Options()
            {
                Confidence = 80,
                Source = "Value",
                Name = "Change",
                ChangeHistoryLength = size
            };
            // Train
            var detector = new IidChangePointEstimator(env, args).Fit(dataView);
            // Transform
            var output = detector.Transform(dataView);
            // Get predictions
            var enumerator = env.Data.CreateEnumerable<Prediction>(output, true).GetEnumerator();
            Prediction row = null;
            List<double> expectedValues = new List<double>() { 0, 5, 0.5, 5.1200000000000114E-08, 0, 5, 0.4999999995, 5.1200000046080209E-08, 0, 5, 0.4999999995, 5.1200000092160303E-08,
                0, 5, 0.4999999995, 5.12000001382404E-08};
            int index = 0;
            while (enumerator.MoveNext() && index < expectedValues.Count)
            {
                row = enumerator.Current;

                Assert.Equal(expectedValues[index++], row.Change[0]);
                Assert.Equal(expectedValues[index++], row.Change[1]);
                Assert.Equal(expectedValues[index++], row.Change[2]);
                Assert.Equal(expectedValues[index++], row.Change[3]);
            }
        }

        [Fact]
        public void ChangePointDetectionWithSeasonality()
        {
            var env = new MLContext(1);
            const int changeHistorySize = 10;
            const int seasonalitySize = 10;
            const int numberOfSeasonsInTraining = 5;
            const int maxTrainingSize = numberOfSeasonsInTraining * seasonalitySize;

            List<Data> data = new List<Data>();
            var dataView = env.Data.LoadFromEnumerable(data);

            var args = new SsaChangePointDetector.Options()
            {
                Confidence = 95,
                Source = "Value",
                Name = "Change",
                ChangeHistoryLength = changeHistorySize,
                TrainingWindowSize = maxTrainingSize,
                SeasonalWindowSize = seasonalitySize
            };

            for (int j = 0; j < numberOfSeasonsInTraining; j++)
                for (int i = 0; i < seasonalitySize; i++)
                    data.Add(new Data(i));

            for (int i = 0; i < changeHistorySize; i++)
                data.Add(new Data(i * 100));

            // Train
            var detector = new SsaChangePointEstimator(env, args).Fit(dataView);
            // Transform
            var output = detector.Transform(dataView);
            // Get predictions
            var enumerator = env.Data.CreateEnumerable<Prediction>(output, true).GetEnumerator();
            Prediction row = null;

            // [TEST_STABILITY]: dotnet core 3.1 generates slightly different result
#if NETCOREAPP3_1
            List<double> expectedValues = new List<double>() { 0, -3.31410551071167, 0.5, 5.12000000000001E-08, 0, 1.570083498954773, 5.2001145245395008E-07,
            0.012414560443710681, 0, 1.2854313850402832, 0.2881081472302483, 0.020389485008225454, 0, -1.0950632095336914, 0.3666388047550645, 0.02695657272695535};
#else
            List<double> expectedValues = new List<double>() { 0, -3.31410598754883, 0.5, 5.12000000000001E-08, 0, 1.5700820684432983, 5.2001145245395008E-07,
            0.012414560443710681, 0, 1.2854313254356384, 0.28810801662678009, 0.02038940454467935, 0, -1.0950627326965332, 0.36663890634019225, 0.026956459625565483};
#endif

            int index = 0;
            while (enumerator.MoveNext() && index < expectedValues.Count)
            {
                row = enumerator.Current;
                Assert.Equal(expectedValues[index++], row.Change[0], precision: 7);  // Alert
                Assert.Equal(expectedValues[index++], row.Change[1], precision: 7);  // Raw score
                Assert.Equal(expectedValues[index++], row.Change[2], precision: 7);  // P-Value score
                Assert.Equal(expectedValues[index++], row.Change[3], precision: 7);  // Martingale score
            }
        }

        [Fact]
        public void ChangePointDetectionWithSeasonalityPredictionEngineNoColumn()
        {
            const int changeHistorySize = 10;
            const int seasonalitySize = 10;
            const int numberOfSeasonsInTraining = 5;
            const int maxTrainingSize = numberOfSeasonsInTraining * seasonalitySize;

            List<Data> data = new List<Data>();

            var ml = new MLContext(seed: 1);
            var dataView = ml.Data.LoadFromEnumerable(data);

            for (int j = 0; j < numberOfSeasonsInTraining; j++)
                for (int i = 0; i < seasonalitySize; i++)
                    data.Add(new Data(i));

            for (int i = 0; i < changeHistorySize; i++)
                data.Add(new Data(i * 100));


            // Pipeline.
            var pipeline = ml.Transforms.Text.FeaturizeText("Text_Featurized", "Text")
                .Append(new SsaChangePointEstimator(ml, new SsaChangePointDetector.Options()
                {
                    Confidence = 95,
                    Source = "Value",
                    Name = "Change",
                    ChangeHistoryLength = changeHistorySize,
                    TrainingWindowSize = maxTrainingSize,
                    SeasonalWindowSize = seasonalitySize
                }));

            // Train.
            var model = pipeline.Fit(dataView);

            //Create prediction function.
            var engine = model.CreateTimeSeriesEngine<Data, Prediction1>(ml);

            //Checkpoint with no inputs passed at prediction.
            var modelPath = "temp.zip";
            engine.CheckPoint(ml, modelPath);

            //Load time series model and we will use this to pass two inputs and compare the raw score 
            //with "engine".
            ITransformer model2 = null;
            using (var file = File.OpenRead(modelPath))
                model2 = ml.Model.Load(file, out var schema);

            //Raw score after state gets updated with two inputs.
            var engine2 = model2.CreateTimeSeriesEngine<Data, Prediction>(ml);
            var prediction2 = engine2.Predict(new Data(1));
            //Raw score after first input.
            Assert.Equal(1.1661833524703979, prediction2.Change[1], precision: 5); // Raw score
            prediction2 = engine2.Predict(new Data(1));
            //Raw score after second input.
            Assert.Equal(0.12216401100158691, prediction2.Change[1], precision: 5); // Raw score

            //Even though time series column is not requested it will 
            // pass the observation through time series transform and update the state with the first input.
            var prediction = engine.Predict(new Data(1));
            Assert.Equal(-1, prediction.Random);

            //Save the model with state updated with just one input.
            engine.CheckPoint(ml, modelPath + 1);
            ITransformer model3 = null;
            using (var file = File.OpenRead(modelPath + 1))
                model3 = ml.Model.Load(file, out var schema);

            //Load the model with state updated with just one input, then pass in the second input
            //and raw score should match the raw score obtained by passing the two input in the first model.
            var engine3 = model3.CreateTimeSeriesEngine<Data, Prediction>(ml);
            var prediction3 = engine3.Predict(new Data(1));
            Assert.Equal(0.12216401100158691, prediction2.Change[1], precision: 5); // Raw score
        }

        [Fact]
        public void ChangePointDetectionWithSeasonalityPredictionEngine()
        {
            const int changeHistorySize = 10;
            const int seasonalitySize = 10;
            const int numberOfSeasonsInTraining = 5;
            const int maxTrainingSize = numberOfSeasonsInTraining * seasonalitySize;

            List<Data> data = new List<Data>();

            var ml = new MLContext(seed: 1);
            var dataView = ml.Data.LoadFromEnumerable(data);

            for (int j = 0; j < numberOfSeasonsInTraining; j++)
                for (int i = 0; i < seasonalitySize; i++)
                    data.Add(new Data(i));

            for (int i = 0; i < changeHistorySize; i++)
                data.Add(new Data(i * 100));


            // Pipeline.
            var pipeline = ml.Transforms.Text.FeaturizeText("Text_Featurized", "Text")
                .Append(ml.Transforms.Conversion.ConvertType("Value", "Value", DataKind.Single))
                .Append(new SsaChangePointEstimator(ml, new SsaChangePointDetector.Options()
                {
                    Confidence = 95,
                    Source = "Value",
                    Name = "Change",
                    ChangeHistoryLength = changeHistorySize,
                    TrainingWindowSize = maxTrainingSize,
                    SeasonalWindowSize = seasonalitySize
                }));

            // Train.
            var model = pipeline.Fit(dataView);

            //Model 1: Prediction #1.
            var engine = model.CreateTimeSeriesEngine<Data, Prediction>(ml);
            var prediction = engine.Predict(new Data(1));
            Assert.Equal(0, prediction.Change[0], precision: 7); // Alert
            Assert.Equal(1.1661833524703979, prediction.Change[1], precision: 5); // Raw score
            Assert.Equal(0.5, prediction.Change[2], precision: 7); // P-Value score
            Assert.Equal(5.1200000000000114E-08, prediction.Change[3], precision: 7); // Martingale score

            //Model 1: Checkpoint.
            var modelPath = "temp.zip";
            engine.CheckPoint(ml, modelPath);

            //Model 1: Prediction #2
            prediction = engine.Predict(new Data(1));
            Assert.Equal(0, prediction.Change[0], precision: 7); // Alert
            Assert.Equal(0.12216401100158691, prediction.Change[1], precision: 5); // Raw score
            Assert.Equal(0.14823824685192111, prediction.Change[2], precision: 5); // P-Value score
            Assert.Equal(1.5292508189989167E-07, prediction.Change[3], precision: 7); // Martingale score

            // Load Model 1.
            ITransformer model2 = null;
            using (var file = File.OpenRead(modelPath))
                model2 = ml.Model.Load(file, out var schema);

            //Predict and expect the same result after checkpointing(Prediction #2).
            engine = model2.CreateTimeSeriesEngine<Data, Prediction>(ml);
            prediction = engine.Predict(new Data(1));
            Assert.Equal(0, prediction.Change[0], precision: 7); // Alert
            Assert.Equal(0.12216401100158691, prediction.Change[1], precision: 5); // Raw score
            Assert.Equal(0.14823824685192111, prediction.Change[2], precision: 5); // P-Value score
            Assert.Equal(1.5292508189989167E-07, prediction.Change[3], precision: 5); // Martingale score
        }

        [Fact]
        public void SsaForecast()
        {
            var env = new MLContext(1);
            const int changeHistorySize = 10;
            const int seasonalitySize = 10;
            const int numberOfSeasonsInTraining = 5;

            List<Data> data = new List<Data>();
            var dataView = env.Data.LoadFromEnumerable(data);

            var args = new SsaForecastingTransformer.Options()
            {
                ConfidenceLevel = 0.95f,
                Source = "Value",
                Name = "Forecast",
                ConfidenceLowerBoundColumn = "MinCnf",
                ConfidenceUpperBoundColumn = "MaxCnf",
                WindowSize = 10,
                SeriesLength = 11,
                TrainSize = 22,
                Horizon = 4,
                IsAdaptive = true
            };

            for (int j = 0; j < numberOfSeasonsInTraining; j++)
                for (int i = 0; i < seasonalitySize; i++)
                    data.Add(new Data(i));

            for (int i = 0; i < changeHistorySize; i++)
                data.Add(new Data(i * 100));

            // Train
            var detector = new SsaForecastingEstimator(env, args).Fit(dataView);
            // Transform
            var output = detector.Transform(dataView);
            // Get predictions
            var enumerator = env.Data.CreateEnumerable<ForecastPrediction>(output, true).GetEnumerator();
            ForecastPrediction row = null;

            // [TEST_STABILITY]: MKL generates different percision float number on Dotnet Core 3.1 
            // and cause the forecast result differs
#if NETCOREAPP3_1
            List<float> expectedForecast = new List<float>() { 0.191492021f, 2.53994060f, 5.26454258f, 7.37313938f };
            List<float> minCnf = new List<float>() { -3.9741986f, -2.36872721f, 0.09407699f, 2.18899393f };
            List<float> maxCnf = new List<float>() { 4.3571825f, 7.4486084f, 10.435008f, 12.5572853f };
#else
            List<float> expectedForecast = new List<float>() { 0.191491723f, 2.53994083f, 5.26454258f, 7.37313938f };
            List<float> minCnf = new List<float>() { -3.9741993f, -2.36872721f, 0.09407653f, 2.18899345f };
            List<float> maxCnf = new List<float>() { 4.3571825f, 7.448609f, 10.435009f, 12.5572853f };
#endif

            enumerator.MoveNext();
            row = enumerator.Current;

            for (int localIndex = 0; localIndex < 4; localIndex++)
            {
                Assert.Equal(expectedForecast[localIndex], row.Forecast[localIndex], precision: 7);
                Assert.Equal(minCnf[localIndex], row.MinCnf[localIndex], precision: 7);
                Assert.Equal(maxCnf[localIndex], row.MaxCnf[localIndex], precision: 7);
            }

        }

        [Fact]
        public void SsaForecastPredictionEngine()
        {
            const int changeHistorySize = 10;
            const int seasonalitySize = 10;
            const int numberOfSeasonsInTraining = 5;

            List<Data> data = new List<Data>();

            var ml = new MLContext(seed: 1);
            var dataView = ml.Data.LoadFromEnumerable(data);

            var args = new SsaForecastingTransformer.Options()
            {
                ConfidenceLevel = 0.95f,
                Source = "Value",
                Name = "Forecast",
                WindowSize = 10,
                SeriesLength = 11,
                TrainSize = 22,
                Horizon = 4,
                ConfidenceLowerBoundColumn = "ConfidenceLowerBound",
                ConfidenceUpperBoundColumn = "ConfidenceUpperBound",
                VariableHorizon = true
            };

            for (int j = 0; j < numberOfSeasonsInTraining; j++)
                for (int i = 0; i < seasonalitySize; i++)
                    data.Add(new Data(i));

            for (int i = 0; i < changeHistorySize; i++)
                data.Add(new Data(i * 100));

            // Train
            var model = ml.Transforms.Text.FeaturizeText("Text_Featurized", "Text")
                .Append(ml.Transforms.Conversion.ConvertType("Value", "Value", DataKind.Single))
                .Append(ml.Forecasting.ForecastBySsa("Forecast", "Value", 10, 11, 22, 4,
                    confidenceLowerBoundColumn: "ConfidenceLowerBound",
                    confidenceUpperBoundColumn: "ConfidenceUpperBound", variableHorizon: true))
                .Append(ml.Transforms.Concatenate("Forecast", "Forecast", "ConfidenceLowerBound", "ConfidenceUpperBound"))
                .Fit(dataView);

            //Prediction engine.
            var engine = model.CreateTimeSeriesEngine<Data, ForecastResultArray>(ml);
            ForecastResultArray result = new ForecastResultArray();

            // Forecast and change the horizon to 5.
            engine.Predict(null, ref result, horizon: 5);
            // [Forecast, ConfidenceLowerBound, ConfidenceUpperBound]

            // [TEST_STABILITY]: dotnet core 3.1 generates slightly different result
#if NETCOREAPP3_1
            Assert.Equal(new float[] { -1.02245092f, 0.08333033f, 2.6073704f, 5.397318f, 7.5008316f, -5.1881413f, -4.82533741f,
                -2.563095f, 0.21317233f, 2.29317045f, 3.1432397f, 4.991998f, 7.777836f, 10.581464f, 12.708492f }, result.Forecast);
#else
            Assert.Equal(new float[] { -1.02245092f, 0.08333081f, 2.60737085f, 5.397319f, 7.500832f, -5.188142f, -4.82533741f,
                -2.563095f, 0.213172823f, 2.29317045f, 3.14324f, 4.991999f, 7.777837f, 10.5814648f, 12.7084932f }, result.Forecast);
#endif

            // Update the forecasting model.
            engine.Predict(new Data(2));

            // Update the model and then forecast.
            engine.Predict(new Data(2), ref result);

            engine.CheckPoint(ml, "model.zip");
            // [Forecast, ConfidenceLowerBound, ConfidenceUpperBound]

            // [TEST_STABILITY]: dotnet core 3.1 generates slightly different result
#if NETCOREAPP3_1
            Assert.Equal(new float[] { 4.310586f, 6.397167f, 7.73934f, 8.029469f, 0.14489543f,
                1.48849952f, 2.5688744f, 2.845323f, 8.476276f, 11.305835f, 12.909805f, 13.2136145f }, result.Forecast);
#else
            Assert.Equal(new float[] { 4.310587f, 6.39716768f, 7.73934f, 8.029469f, 0.144895911f,
                1.48849952f, 2.568874f, 2.84532261f, 8.476278f, 11.3058357f, 12.9098063f, 13.2136145f }, result.Forecast);
#endif

            // Checkpoint the model.
            ITransformer modelCopy;
            using (var file = File.OpenRead("model.zip"))
                modelCopy = ml.Model.Load(file, out DataViewSchema schema);

            // We must create a new prediction engine from the persisted model.
            var forecastEngineCopy = modelCopy.CreateTimeSeriesEngine<Data, ForecastResultArray>(ml);
            ForecastResultArray resultCopy = new ForecastResultArray();

            // Update both the models.
            engine.Predict(new Data(3));
            forecastEngineCopy.Predict(new Data(3));

            // Forecast values with the original and check-pointed model.
            forecastEngineCopy.Predict(null, ref resultCopy, horizon: 5);
            engine.Predict(null, ref result, horizon: 5);
            // [Forecast, ConfidenceLowerBound, ConfidenceUpperBound]

            // [TEST_STABILITY]: dotnet core 3.1 generates slightly different result
#if NETCOREAPP3_1
            Assert.Equal(new float[] { 6.006588f, 7.506871f, 7.964249f, 7.1751432f,
                5.0265527f, 1.84089744f, 2.5982034f, 2.7937837f, 1.9909977f,
                -0.1811084f, 10.172278f, 12.415539f, 13.1347151f, 12.359289f, 10.234214f}, result.Forecast);
#else
            Assert.Equal(new float[] { 6.00658846f, 7.506871f, 7.96424866f, 7.17514229f,
                5.02655172f, 1.84089744f, 2.59820318f, 2.79378271f, 1.99099624f,
            -0.181109816f, 10.1722794f, 12.41554f, 13.1347151f, 12.3592882f, 10.2342129f}, result.Forecast);
#endif

            // The forecasted results should be the same because the state of the models
            // is the same.
            Assert.Equal(result.Forecast, resultCopy.Forecast);

        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AnomalyDetectionWithSrCnn(bool loadDataFromFile)
        {
            var ml = new MLContext(1);
            IDataView dataView;
            if (loadDataFromFile)
            {
                var dataPath = GetDataPath(Path.Combine("Timeseries", "anomaly_detection.csv"));

                // Load data from file into the dataView
                dataView = ml.Data.LoadFromTextFile(dataPath, new[] {
                    new TextLoader.Column("Value", DataKind.Single, 0),
                }, hasHeader: true);
            }
            else
            {
                // Generate sample series data with an anomaly
                var data = new List<TimeSeriesData>();
                for (int index = 0; index < 20; index++)
                {
                    data.Add(new TimeSeriesData(5));
                }
                data.Add(new TimeSeriesData(10));
                for (int index = 0; index < 5; index++)
                {
                    data.Add(new TimeSeriesData(5));
                }

                // Convert data to IDataView.
                dataView = ml.Data.LoadFromEnumerable(data);
            }

            // Setup the estimator arguments
            string outputColumnName = nameof(SrCnnAnomalyDetection.Prediction);
            string inputColumnName = nameof(TimeSeriesData.Value);

            // The transformed data.
            var transformedData = ml.Transforms.DetectAnomalyBySrCnn(outputColumnName, inputColumnName, 16, 5, 5, 3, 8, 0.35).Fit(dataView).Transform(dataView);

            // Getting the data of the newly created column as an IEnumerable of SrCnnAnomalyDetection.
            var predictionColumn = ml.Data.CreateEnumerable<SrCnnAnomalyDetection>(transformedData, reuseRowObject: false);

            int k = 0;
            foreach (var prediction in predictionColumn)
            {
                if (k == 20)
                    Assert.Equal(1, prediction.Prediction[0]);
                else
                    Assert.Equal(0, prediction.Prediction[0]);
                k += 1;
            }
        }

        [Theory, CombinatorialData]
        public void TestSrCnnBatchAnomalyDetector(
            [CombinatorialValues(SrCnnDetectMode.AnomalyOnly, SrCnnDetectMode.AnomalyAndExpectedValue, SrCnnDetectMode.AnomalyAndMargin)] SrCnnDetectMode mode,
            [CombinatorialValues(true, false)] bool loadDataFromFile,
            [CombinatorialValues(-1, 24, 26, 512)] int batchSize)
        {
            var ml = new MLContext(1);
            IDataView dataView;
            if (loadDataFromFile)
            {
                var dataPath = GetDataPath("Timeseries", "anomaly_detection.csv");

                // Load data from file into the dataView
                dataView = ml.Data.LoadFromTextFile<TimeSeriesDataDouble>(dataPath, hasHeader: true);
            }
            else
            {
                // Generate sample series data with an anomaly
                var data = new List<TimeSeriesDataDouble>();
                for (int index = 0; index < 20; index++)
                {
                    data.Add(new TimeSeriesDataDouble { Value = 5 });
                }
                data.Add(new TimeSeriesDataDouble { Value = 10 });
                for (int index = 0; index < 5; index++)
                {
                    data.Add(new TimeSeriesDataDouble { Value = 5 });
                }

                // Convert data to IDataView.
                dataView = ml.Data.LoadFromEnumerable(data);
            }

            // Setup the detection arguments
            string outputColumnName = nameof(SrCnnAnomalyDetection.Prediction);
            string inputColumnName = nameof(TimeSeriesDataDouble.Value);

            // Do batch anomaly detection
            var outputDataView = ml.AnomalyDetection.DetectEntireAnomalyBySrCnn(dataView, outputColumnName, inputColumnName,
                threshold: 0.35, batchSize: batchSize, sensitivity: 90.0, mode);

            // Getting the data of the newly created column as an IEnumerable of
            // SrCnnAnomalyDetection.
            var predictionColumn = ml.Data.CreateEnumerable<SrCnnAnomalyDetection>(
                outputDataView, reuseRowObject: false);

            int k = 0;
            foreach (var prediction in predictionColumn)
            {
                switch (mode)
                {
                    case SrCnnDetectMode.AnomalyOnly:
                        Assert.Equal(3, prediction.Prediction.Length);
                        if (k == 20)
                            Assert.Equal(1, prediction.Prediction[0]);
                        else
                            Assert.Equal(0, prediction.Prediction[0]);
                        break;
                    case SrCnnDetectMode.AnomalyAndExpectedValue:
                        Assert.Equal(4, prediction.Prediction.Length);
                        if (k == 20)
                        {
                            Assert.Equal(1, prediction.Prediction[0]);
                            Assert.Equal(5.00, prediction.Prediction[3], 2);
                        }
                        else
                            Assert.Equal(0, prediction.Prediction[0]);
                        break;
                    case SrCnnDetectMode.AnomalyAndMargin:
                        Assert.Equal(7, prediction.Prediction.Length);
                        if (k == 20)
                        {
                            Assert.Equal(1, prediction.Prediction[0]);
                            Assert.Equal(5.00, prediction.Prediction[3], 2);
                            Assert.Equal(5.00, prediction.Prediction[4], 2);
                            Assert.Equal(5.01, prediction.Prediction[5], 2);
                            Assert.Equal(4.99, prediction.Prediction[6], 2);
                        }
                        else
                            Assert.Equal(0, prediction.Prediction[0]);
                        break;
                }
                k += 1;
            }
        }

        [Fact]
        public void RootCauseLocalization()
        {
            // Create an root cause localizatiom input
            var rootCauseLocalizationInput = new RootCauseLocalizationInput(GetRootCauseTimestamp(), GetRootCauseAnomalyDimension(), new List<MetricSlice>() { new MetricSlice(GetRootCauseTimestamp(), GetRootCauseLocalizationPoints()) }, AggregateType.Sum, _rootCauseAggSymbol);

            var ml = new MLContext(1);
            RootCause rootCause = ml.AnomalyDetection.LocalizeRootCause(rootCauseLocalizationInput);

            Assert.NotNull(rootCause);
            Assert.Equal(1, (int)rootCause.Items.Count);
            Assert.Equal(3, (int)rootCause.Items[0].Dimension.Count);
            Assert.Equal(AnomalyDirection.Up, rootCause.Items[0].Direction);
            Assert.Equal(1, (int)rootCause.Items[0].Path.Count);
            Assert.Equal("DataCenter", rootCause.Items[0].Path[0]);

            Dictionary<string, Object> expectedDim = new Dictionary<string, Object>();
            expectedDim.Add("Country", "UK");
            expectedDim.Add("DeviceType", _rootCauseAggSymbol);
            expectedDim.Add("DataCenter", "DC1");

            foreach (KeyValuePair<string, object> pair in rootCause.Items[0].Dimension)
            {
                Assert.Equal(expectedDim[pair.Key], pair.Value);
            }
        }

        private static List<TimeSeriesPoint> GetRootCauseLocalizationPoints()
        {
            List<TimeSeriesPoint> points = new List<TimeSeriesPoint>();

            Dictionary<string, Object> dic1 = new Dictionary<string, Object>();
            dic1.Add("Country", "UK");
            dic1.Add("DeviceType", "Laptop");
            dic1.Add("DataCenter", "DC1");
            points.Add(new TimeSeriesPoint(200, 100, true, dic1));

            Dictionary<string, Object> dic2 = new Dictionary<string, Object>();
            dic2.Add("Country", "UK");
            dic2.Add("DeviceType", "Mobile");
            dic2.Add("DataCenter", "DC1");
            points.Add(new TimeSeriesPoint(1000, 100, true, dic2));

            Dictionary<string, Object> dic3 = new Dictionary<string, Object>();
            dic3.Add("Country", "UK");
            dic3.Add("DeviceType", _rootCauseAggSymbol);
            dic3.Add("DataCenter", "DC1");
            points.Add(new TimeSeriesPoint(1200, 200, true, dic3));

            Dictionary<string, Object> dic4 = new Dictionary<string, Object>();
            dic4.Add("Country", "UK");
            dic4.Add("DeviceType", "Laptop");
            dic4.Add("DataCenter", "DC2");
            points.Add(new TimeSeriesPoint(100, 100, false, dic4));

            Dictionary<string, Object> dic5 = new Dictionary<string, Object>();
            dic5.Add("Country", "UK");
            dic5.Add("DeviceType", "Mobile");
            dic5.Add("DataCenter", "DC2");
            points.Add(new TimeSeriesPoint(200, 200, false, dic5));

            Dictionary<string, Object> dic6 = new Dictionary<string, Object>();
            dic6.Add("Country", "UK");
            dic6.Add("DeviceType", _rootCauseAggSymbol);
            dic6.Add("DataCenter", "DC2");
            points.Add(new TimeSeriesPoint(300, 300, false, dic6));

            Dictionary<string, Object> dic7 = new Dictionary<string, Object>();
            dic7.Add("Country", "UK");
            dic7.Add("DeviceType", _rootCauseAggSymbol);
            dic7.Add("DataCenter", _rootCauseAggSymbol);
            points.Add(new TimeSeriesPoint(1500, 500, true, dic7));

            Dictionary<string, Object> dic8 = new Dictionary<string, Object>();
            dic8.Add("Country", "UK");
            dic8.Add("DeviceType", "Laptop");
            dic8.Add("DataCenter", _rootCauseAggSymbol);
            points.Add(new TimeSeriesPoint(300, 200, true, dic8));

            Dictionary<string, Object> dic9 = new Dictionary<string, Object>();
            dic9.Add("Country", "UK");
            dic9.Add("DeviceType", "Mobile");
            dic9.Add("DataCenter", _rootCauseAggSymbol);
            points.Add(new TimeSeriesPoint(1200, 300, true, dic9));

            return points;
        }

        private static Dictionary<string, Object> GetRootCauseAnomalyDimension()
        {
            Dictionary<string, Object> dim = new Dictionary<string, Object>();
            dim.Add("Country", "UK");
            dim.Add("DeviceType", _rootCauseAggSymbol);
            dim.Add("DataCenter", _rootCauseAggSymbol);

            return dim;
        }

        private static DateTime GetRootCauseTimestamp()
        {
            return new DateTime(2020, 3, 23, 0, 0, 0);
        }
    }
}
