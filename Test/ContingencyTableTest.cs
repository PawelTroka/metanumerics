﻿using System;
using System.Collections.Generic;

using TestClassAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
using TestMethodAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
using ExpectedExceptionAttribute = Microsoft.VisualStudio.TestTools.UnitTesting.ExpectedExceptionAttribute;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

using Meta.Numerics;
using Meta.Numerics.Data;
using Meta.Numerics.Statistics;
using Meta.Numerics.Statistics.Distributions;

namespace Test {

    [TestClass]
    public class ContingencyTableTest {

        [TestMethod]
        public void ContingencyTableProbabilities () {

            // Construct data where (i) there are both reference-nulls and nullable-struct-nulls,
            // (ii) all values of one column are equally, (iii) values of other column depend on value of first column
            List<string> groups = new List<string>(){ "A", "B", "C", null };

            FrameTable data = new FrameTable();
            data.AddColumn<string>("Group");
            data.AddColumn<bool?>("Outcome");

            int n = 512;
            double pOutcomeNull = 0.05;
            Func<int, double> pOutcome = groupIndex => 0.8 - 0.2 * groupIndex; 
            Random rng = new Random(10101010);
            for (int i = 0; i < n; i++) {
                int groupIndex = rng.Next(0, groups.Count);
                string group = groups[groupIndex];
                bool? outcome = (rng.NextDouble() < pOutcome(groupIndex));
                if (rng.NextDouble() < pOutcomeNull) outcome = null;
                data.AddRow(group, outcome);
            }

            // Form a contingency table.
            ContingencyTable<string, bool?> table = Bivariate.Crosstabs(data["Group"].As<string>(), data["Outcome"].As<bool?>());

            // Total counts should match
            Assert.IsTrue(table.Total == n);

            // All values should be represented
            foreach (string row in table.Rows) Assert.IsTrue(groups.Contains(row));

            // Counts in each cell and marginal totals should match
            foreach (string group in table.Rows) {
                int rowTotal = 0;
                foreach (bool? outcome in table.Columns) {
                    FrameView view = data.Where(r => ((string) r["Group"] == group) && ((bool?) r["Outcome"] == outcome));
                    Assert.IsTrue(table[group, outcome] == view.Rows.Count);
                    rowTotal += view.Rows.Count;
                }
                Assert.IsTrue(rowTotal == table.RowTotal(group));
            }

            // Inferred probabilities should agree with model
            Assert.IsTrue(table.ProbabilityOfColumn(null).ConfidenceInterval(0.99).ClosedContains(pOutcomeNull));
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++) {
                string group = groups[groupIndex];
                Assert.IsTrue(table.ProbabilityOfRow(group).ConfidenceInterval(0.99).ClosedContains(0.25));
                Assert.IsTrue(table.ProbabilityOfColumnConditionalOnRow(true, group).ConfidenceInterval(0.99).ClosedContains(pOutcome(groupIndex) * (1.0 - pOutcomeNull)));
            }
            Assert.IsTrue(table.ProbabilityOfColumn(null).ConfidenceInterval(0.99).ClosedContains(pOutcomeNull));

            // Pearson test should catch that rows and columns are corrleated
            Assert.IsTrue(table.PearsonChiSquaredTest().Probability < 0.05);

        }

        [TestMethod]
        public void ContingencyTableOperations () {

            ContingencyTable t = new ContingencyTable(4, 3);
            Assert.IsTrue(t.Rows.Count == 4);
            Assert.IsTrue(t.Columns.Count == 3);

            Assert.IsTrue(t.RowTotal(2) == 0);
            Assert.IsTrue(t.ColumnTotal(1) == 0);
            Assert.IsTrue(t.Total == 0);

            t[1, 1] = 2;
            Assert.IsTrue(t[1, 1] == 2);
            Assert.IsTrue(t.RowTotal(2) == 0);
            Assert.IsTrue(t.ColumnTotal(1) == 2);
            Assert.IsTrue(t.Total == 2);

            t.Increment(2, 1);
            Assert.IsTrue(t[2, 1] == 1);
            Assert.IsTrue(t.RowTotal(2) == 1);
            Assert.IsTrue(t.ColumnTotal(1) == 3);
            Assert.IsTrue(t.Total == 3);

            t.Decrement(1, 1);
            Assert.IsTrue(t[1, 1] == 1);
            Assert.IsTrue(t.RowTotal(2) == 1);
            Assert.IsTrue(t.ColumnTotal(1) == 2);
            Assert.IsTrue(t.Total == 2);

        }

        private void ChooseRandomCell (double[,] pp, double p, out int r, out int c) {
            double ps = 0.0;
            r = 0; c = 0;
            while (r < pp.GetLength(0)) {
                c = 0;
                while (c < pp.GetLength(1)) {
                    ps += pp[r, c];
                    if (ps >= p) return;
                    c++;
                }
                r++;
            }
        }

        [TestMethod]
        public void ContingencyTableProbabilitiesAndUncertainties () {

            // start with an underlying population
            double[,] pp = new double[,]
                { { 1.0 / 45.0, 2.0 / 45.0, 3.0 / 45.0 },
                  { 4.0 / 45.0, 5.0 / 45.0, 6.0 / 45.0 },
                  { 7.0 / 45.0, 8.0 / 45.0, 9.0 / 45.0 } };

            // form 50 contingency tables, each with N = 50
            Random rng = new Random(314159);
            BivariateSample p22s = new BivariateSample();
            BivariateSample pr0s = new BivariateSample();
            BivariateSample pc1s = new BivariateSample();
            BivariateSample pr2c0s = new BivariateSample();
            BivariateSample pc1r2s = new BivariateSample();
            for (int i = 0; i < 50; i++) {

                ContingencyTable T = new ContingencyTable(3, 3);
                for (int j = 0; j < 50; j++) {
                    int r, c;
                    ChooseRandomCell(pp, rng.NextDouble(), out r, out c);
                    T.Increment(r, c);
                }

                Assert.IsTrue(T.Total == 50);

                // for each contingency table, compute estimates of various population quantities

                UncertainValue p22 = T.ProbabilityOf(2, 2);
                UncertainValue pr0 = T.ProbabilityOfRow(0);
                UncertainValue pc1 = T.ProbabilityOfColumn(1);
                UncertainValue pr2c0 = T.ProbabilityOfRowConditionalOnColumn(2, 0);
                UncertainValue pc1r2 = T.ProbabilityOfColumnConditionalOnRow(1, 2);
                p22s.Add(p22.Value, p22.Uncertainty);
                pr0s.Add(pr0.Value, pr0.Uncertainty);
                pc1s.Add(pc1.Value, pc1.Uncertainty);
                pr2c0s.Add(pr2c0.Value, pr2c0.Uncertainty);
                pc1r2s.Add(pc1r2.Value, pc1r2.Uncertainty);

            }

            // the estimated population mean of each probability should include the correct probability in the underlyting distribution
            Assert.IsTrue(p22s.X.PopulationMean.ConfidenceInterval(0.95).ClosedContains(9.0 / 45.0));
            Assert.IsTrue(pr0s.X.PopulationMean.ConfidenceInterval(0.95).ClosedContains(6.0 / 45.0));
            Assert.IsTrue(pc1s.X.PopulationMean.ConfidenceInterval(0.95).ClosedContains(15.0 / 45.0));
            Assert.IsTrue(pr2c0s.X.PopulationMean.ConfidenceInterval(0.95).ClosedContains(7.0 / 12.0));
            Assert.IsTrue(pc1r2s.X.PopulationMean.ConfidenceInterval(0.95).ClosedContains(8.0 / 24.0));

            // the estimated uncertainty for each population parameter should be the standard deviation across independent measurements
            // since the reported uncertainly changes each time, we use the mean value for comparison
            Assert.IsTrue(p22s.X.PopulationStandardDeviation.ConfidenceInterval(0.95).ClosedContains(p22s.Y.Mean));
            Assert.IsTrue(pr0s.X.PopulationStandardDeviation.ConfidenceInterval(0.95).ClosedContains(pr0s.Y.Mean));
            Assert.IsTrue(pc1s.X.PopulationStandardDeviation.ConfidenceInterval(0.95).ClosedContains(pc1s.Y.Mean));
            Assert.IsTrue(pr2c0s.X.PopulationStandardDeviation.ConfidenceInterval(0.95).ClosedContains(pr2c0s.Y.Mean));
            Assert.IsTrue(pc1r2s.X.PopulationStandardDeviation.ConfidenceInterval(0.95).ClosedContains(pc1r2s.Y.Mean));

        }

        [TestMethod]
        public void McNemarTestDistribution () {

            // Define a population and the accuracy of two tests for a condition
            double fractionPositive = 0.4;
            double aAccuracy = 0.2;
            double bAccuracy = 0.9;

            // Form a bunch of samples; we will run a McNemar test on each
            List<double> statistics = new List<double>();
            ContinuousDistribution distribution = null;
            Random rng = new Random(1);
            for (int i = 0; i < 32; i++) {

                // Run a and b tests on each person.
                List<bool> aResults = new List<bool>();
                List<bool> bResults = new List<bool>();
                for (int j = 0; j < 64; j++) {
                    bool isPositive = rng.NextDouble() < fractionPositive;
                    bool aResult = rng.NextDouble() < aAccuracy ? isPositive : !isPositive;
                    aResults.Add(aResult);
                    bool bResult = rng.NextDouble() < bAccuracy ? isPositive : !isPositive;
                    bResults.Add(bResult);
                }

                // Do a McNemar test to determine whether tests are differently weighted.
                // By our construction, they shouldn't be.
                ContingencyTable<bool, bool> table = Bivariate.Crosstabs(aResults, bResults);
                TestResult result = table.Binary.McNemarTest();
                statistics.Add(result.Statistic.Value);
                distribution = result.Statistic.Distribution;

            }

            // Since the null hypothesis is satisfied, the test statistic distribution should
            // match the claimed null distribution.
            TestResult test = statistics.KolmogorovSmirnovTest(distribution);
            Assert.IsTrue(test.Probability > 0.05);
        }

#if FUTURE

        [TestMethod]
        public void ContingencyTableNamedOperations () {

            ContingencyTable t = new ContingencyTable(2, 3);
            t.RowNames[0] = "Male";
            t.RowNames[1] = "Female";
            t.ColumnNames[0] = "Party 1";
            t.ColumnNames[1] = "Party 2";
            t.ColumnNames[2] = "Party 3";
            t["Male", "Party 1"] = 10;
            t["Male", "Party 2"] = 20;
            t["Male", "Party 3"] = 30;
            t["Female", "Party 1"] = 30;
            t["Female", "Party 2"] = 20;
            t["Female", "Party 3"] = 10;

        }

#endif

    }
}
