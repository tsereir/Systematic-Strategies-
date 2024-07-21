using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PricingLibrary;
using PricingLibrary.Computations;
using PricingLibrary.DataClasses;
using PricingLibrary.MarketDataFeed;
using PricingLibrary.TimeHandler;
using CsvHelper.Configuration;
using CsvHelper;
using System.Globalization;
using static System.Collections.Specialized.BitVector32;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Net;
using System.Text;
using UAParser;
using PricingLibrary.RebalancingOracleDescriptions;
using PricingLibrary.Utilities;
using MathNet.Numerics.LinearAlgebra;
using static System.Net.Mime.MediaTypeNames;
using Microsoft.VisualBasic;

namespace Application_Couverture
{
    abstract class RebalancingOracle
    {
        public abstract bool rebalancingDate(DateTime d);
    }

    class RegularOracle : RebalancingOracle
    {
        public int Period;
        public int compteur = 0;
        public RegularOracle(IRebalancingOracleDescription description)
        {
            RegularOracleDescription regularOracle = (RegularOracleDescription)description;
            this.Period = regularOracle.Period;
        }
        public override bool rebalancingDate(DateTime d)
        {
            compteur += 1;

            if ((compteur)  % Period == 0)
            {
                return true;
            }
            return false;
        }

    }

    class WeeklyOracle : RebalancingOracle
    {
        public DayOfWeek RebalancingDay { get; set; }

        public WeeklyOracle(IRebalancingOracleDescription description)
        {
            WeeklyOracleDescription weeklyOracle = (WeeklyOracleDescription)description;
            this.RebalancingDay = weeklyOracle.RebalancingDay;
        }
        public override bool rebalancingDate(DateTime d)
        {
            if (d.DayOfWeek == this.RebalancingDay)
            {
                return true;
            }
            return false;
        }

    }


    class Portfolio
        {

            public double Cash { get; set; }
            public double PortfolioValue { get; set; }
            public DateTime Date { get; set; }

            public RebalancingOracle Description { get; set; }

        public Portfolio(double cash, double portfolioValue, DateTime date, RebalancingOracle description)
        {
            Cash = cash;
            PortfolioValue = portfolioValue;
            Date = date;
            Description = description;
        }



        public void Initialize(double[][] initialSpots, double[][] initialDeltas, double initialOptionPrice)
            {
                this.PortfolioValue = initialOptionPrice;
                double somme = 0;
                for (int i = 0; i < initialSpots.Length; i++)
                {
                    for (int j = 0; j < initialSpots[i].Length; j++)
                    {
                        somme += initialDeltas[i][j] * initialSpots[i][j];
                    }
                }
                this.Cash = PortfolioValue - somme;

            }
            public void updateComp(double[][] spots, double[][] oldDeltas, double[][] newDeltas, DateTime endDate)
            {
                double somme = 0;
                double somme2 = 0;
                if (spots.Length == oldDeltas.Length)
                {
                    for (int i = 0; i < spots.Length; ++i)
                    {
                        for (int j = 0; j < spots[i].Length; ++j)
                        {
                            somme += oldDeltas[i][j] * spots[i][j];
                            somme2 += newDeltas[i][j] * spots[i][j];
                        }
                    }
                    double r = RiskFreeRateProvider.GetRiskFreeRateAccruedValue(this.Date, endDate);
                    double riskFreeCash = Cash * r;
                    somme += riskFreeCash;
                    this.Cash = somme - somme2;
                }
                else
                {
                    Console.WriteLine("Erreur : Les tableaux spots et deltas n'ont pas la même longueur.");
                }
            }
            public double UpdatePortfolioValue(double[][] spotPrices, double[][] newDeltas, DateTime endDate)
            {
                double somme1 = 0;
                for (int i = 0; i < spotPrices.Length; i++)
                {
                    for (int j = 0; j < spotPrices[i].Length; j++)
                    {
                        somme1 += (spotPrices[i][j] * newDeltas[i][j]);
                    }
                }
            double r = RiskFreeRateProvider.GetRiskFreeRateAccruedValue(this.Date, endDate);
            this.Cash = this.Cash * r;
            this.PortfolioValue = somme1 + this.Cash;
            return PortfolioValue;
            }

            public bool rebalancingTime(DateTime d)
            {
                return this.Description.rebalancingDate(d);
            }
    }
    static class Program
        {

        public static IEnumerable<ShareValue> LoadMarketData(string filePath)
        {
            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true, // Le fichier CSV a une en-tête
                Delimiter = "," // Le délimiteur de champ (à adapter en fonction du format)
            };

            try
            {
                using (var reader = new StreamReader(filePath))
                using (var csv = new CsvReader(reader, csvConfig))
                {
                    // Ajoutez cette ligne pour configurer le format de date
                    csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>().Formats = new[] { "MM/dd/yyyy HH:mm:ss" };

                    var records = csv.GetRecords<ShareValue>().ToList();
                    return records;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while loading market data: " + ex.Message);
                return new List<ShareValue>(); // Retourne une liste vide en cas d'erreur
            }
        }

        public static BasketTestParameters? LoadTestParameters(string filePath)
{
    try
    {
        var options = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(), new RebalancingOracleDescriptionConverter() }
        };
        string json = File.ReadAllText(filePath);
        BasketTestParameters? testParameters = JsonSerializer.Deserialize<BasketTestParameters>(json, options);
        return testParameters;
    }
    catch (Exception ex)
    {
        Console.WriteLine("An error occurred while loading test parameters: " + ex.Message);
        return null; 
    }
}
        static void WriteOutputDataToJsonFile(List<OutputData> outputDataList, string output)
            {
                var options = new JsonSerializerOptions()
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new JsonStringEnumConverter(), new RebalancingOracleDescriptionConverter() }
                };

                string json = JsonSerializer.Serialize(outputDataList, options);
                File.WriteAllText(output, json);
            }

        static void Main(string[] args)
        {
            var testParameters = LoadTestParameters(args[0]);
            var shareValues = LoadMarketData(args[1]);    

            List<OutputData> outputDataList = new List<OutputData>();

             if (args.Length != 3)
            {
                Console.WriteLine("Usage: <test-params> <mkt-data> <output-file>");
                return;
            }
            var maturity = testParameters.BasketOption.Maturity;
            var oracle = testParameters.RebalancingOracleDescription;
            RebalancingOracle rebalancingOracle;

            var dataFeeds = shareValues.GroupBy(d => d.DateOfPrice,
                t => new { Symb = t.Id.Trim(), Val = t.Value },
                (key, g) => new DataFeed(key, g.ToDictionary(e => e.Symb, e => e.Val))).ToList();

            Pricer pricer = new Pricer(testParameters);
            var firstDate = dataFeeds[0].Date;

            
            if (oracle.Type.ToString() == "Weekly")
            {
                rebalancingOracle = new WeeklyOracle(oracle);
            }
            else
            {
                rebalancingOracle = new RegularOracle(oracle);
            }
            Portfolio p = new Portfolio(0, 0, firstDate, rebalancingOracle);

            var timeToMaturity0 = MathDateConverter.ConvertToMathDistance(firstDate, maturity);


            List<double> InitSpots = testParameters.BasketOption.UnderlyingShareIds
            .Where(id => dataFeeds[0].PriceList.ContainsKey(id))
            .Select(id => dataFeeds[0].PriceList[id])
            .ToList();


            var initPricingResults = pricer.Price(timeToMaturity0,
               InitSpots.ToArray());

            p.Initialize(new double[][] { InitSpots.ToArray() },
                new double[][] { initPricingResults.Deltas.ToArray() },
                initPricingResults.Price);

            p.Date = firstDate;
            var oldDeltas = new double[][] { initPricingResults.Deltas.ToArray() };

            OutputData firstOutputData = new OutputData
            {
                Date = firstDate,
                Price = initPricingResults.Price,
                Deltas = initPricingResults.Deltas.ToArray(),
                DeltasStdDev = initPricingResults.DeltaStdDev,
                PriceStdDev = initPricingResults.PriceStdDev,
                Value = p.PortfolioValue,
            };
            outputDataList.Add(firstOutputData);

            foreach (DataFeed dataFeed in dataFeeds.Skip(1))
            {
                var valuesList =  testParameters.BasketOption.UnderlyingShareIds
                .Where(id => dataFeed.PriceList.ContainsKey(id))
                .Select(id => dataFeed.PriceList[id])
                .ToList();

                var currentDate = dataFeed.Date;
                var timeToMaturity = MathDateConverter.ConvertToMathDistance(currentDate, maturity);

                // On ne créer des objets outputdata que si on rebalance

                if (p.rebalancingTime(currentDate))
                {
                    var pricingResults = pricer.Price(timeToMaturity, valuesList.ToArray());
                    var deltas = pricingResults.Deltas.ToArray();

                    p.updateComp(new double[][] { valuesList.ToArray() }, oldDeltas, new double[][] { deltas }, currentDate);
                    p.UpdatePortfolioValue(new double[][] { valuesList.ToArray() }, new double[][] { deltas.ToArray() }, currentDate);
                    p.Date = currentDate;
                    OutputData outputData = new OutputData
                    {                       
                        Date = currentDate,
                        Price = pricingResults.Price,
                        Deltas = deltas.ToArray(),
                        DeltasStdDev = pricingResults.DeltaStdDev,
                        PriceStdDev = pricingResults.PriceStdDev,
                        Value = p.PortfolioValue,
                    };
                    outputDataList.Add(outputData);
                    oldDeltas = new double[][] { deltas };
                }
            }
            WriteOutputDataToJsonFile(outputDataList, args[2]);
        }
    }
}
