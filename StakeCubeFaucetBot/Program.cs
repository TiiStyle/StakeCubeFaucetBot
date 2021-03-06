﻿using Newtonsoft.Json;
using RestSharp;
using System;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

namespace RealStakeCubeFaucetBot
{
    class Program
    {
        static bool botStop = false;
        static string decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        static string CFDUID = "", PHPSESSID = "", input = "";

        static Task Main(string[] args)
        {
            Console.Title = "StakeCube Faucet Bot";
            Console.Write("Please enter your CFDUID: ");
            CFDUID = Console.ReadLine();
            Console.Clear();
            Console.Write("Please enter your PHPSESSID: ");
            PHPSESSID = Console.ReadLine();

            while (true)
            {
                Console.Clear();
                Console.WriteLine("Start Bot? y/n/q");
                input = Console.ReadLine();
                if (input != "y" && input != "n" && input != "q")
                {
                    continue;
                }
                else if (input == "q" || input == "n")
                    Environment.Exit(-1);
                else if (input == "y")
                    Bot_Run();
            }
        }

        private static void Bot_Run()
        {
            int nextClaimIn;
            while (!botStop)
            {
                //Look for claims
                IRestResponse response = GetFaucets(CFDUID, PHPSESSID);
                nextClaimIn = 86400 - Convert.ToInt32(GetNextClaimTime(response.Content));
                Console.Clear();
                if (nextClaimIn > 5)
                {
                    //No claims found
                    TimeSpan span = new TimeSpan(0, 0, nextClaimIn);
                    Console.WriteLine($"Bot-Status: running. Next claim in: {span.Hours}:{span.Minutes}:{span.Seconds}");
                }
                else
                {
                    Console.WriteLine("Bot-Status: Claiming.");
                    if (nextClaimIn <= 0)
                    {
                        //Claim now
                        ClaimFaucets(CFDUID, PHPSESSID);
                    }
                }

                if (nextClaimIn > 3600)
                    System.Threading.Thread.Sleep(60000);
                else if (nextClaimIn > 600)
                    System.Threading.Thread.Sleep(10000);
                else if (nextClaimIn > 5)
                    System.Threading.Thread.Sleep(5000);
                else if (nextClaimIn < 0)
                    System.Threading.Thread.Sleep(1000);
                else if (nextClaimIn <= 5)
                    System.Threading.Thread.Sleep(nextClaimIn * 1000 + 999);

            }
        }

        private static long GetNextClaimTime(string content)
        {
            long highestDiff = long.MinValue;

            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include
            };
            var dynamic = JsonConvert.DeserializeObject<dynamic>(content, settings);

            foreach (var token in dynamic)
            {
                Faucet faucet = new Faucet();
                ConfigureFaucet(ref faucet, token);

                double balance;
                double amountPerClaim;
                if (decimalSeparator != ".")
                {
                    balance = Convert.ToDouble(faucet.BALANCE.Replace('.', ','));
                    amountPerClaim = Convert.ToDouble(faucet.AMOUNT_PER_CLAIM.Replace('.', ','));
                }
                else
                {
                    balance = Convert.ToDouble(faucet.BALANCE);
                    amountPerClaim = Convert.ToDouble(faucet.AMOUNT_PER_CLAIM);
                }

                if (amountPerClaim <= balance && (faucet.DIFF_IN_SEC > highestDiff || faucet.DIFF_IN_SEC == null))
                {
                    if (faucet.DIFF_IN_SEC == null)
                        highestDiff = 86400;
                    else
                        highestDiff = Convert.ToInt64(faucet.DIFF_IN_SEC);
                }
            }
            return highestDiff;
        }

        private static void ConfigureFaucet(ref Faucet faucetToConfigure, dynamic token)
        {
            foreach (var childToken in token)
            {
                PropertyInfo[] properties = typeof(Faucet).GetProperties();
                foreach (PropertyInfo property in properties)
                {
                    if (property.Name.Equals(childToken.Name))
                    {
                        property.SetValue(faucetToConfigure, childToken.Value.Value);
                        break;
                    }
                }
            }
        }

        private static IRestResponse GetFaucets(string cfduid, string phpsessid)
        {
            IRestResponse response;
            while (true)
            {
                var client = new RestClient("https://stakecube.net/app/community/functions")
                {
                    Timeout = 10000
                };
                var request = new RestRequest(Method.POST);
                request.AddHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:75.0) Gecko/20100101 Firefox/75.0");
                request.AddHeader("X-Requested-With", "XMLHttpRequest");
                request.AddHeader("Content-Type", "application/x-www-form-urlencoded; charset=UTF-8");
                request.AddHeader("Referer", "https://stakecube.net/app/community/faucets");
                request.AddHeader("TE", "Trailers");
                request.AddHeader("Cookie", $"PHPSESSID={phpsessid}; __cfduid={cfduid}");
                request.AddParameter("ACTION", "GET_FAUCETS");
                response = client.Execute(request);
                if (!response.IsSuccessful)
                    continue;
                else
                    break;
            }
            return response;
        }

        private static void ClaimFaucets(string cfduid, string phpsessid)
        {
            Faucet nextToClaim;
            while ((nextToClaim = NextClaimableFaucet(cfduid, phpsessid)) != null)
            {
                RequestFaucetClaim(nextToClaim, cfduid, phpsessid);
            }
        }

        private static IRestResponse RequestFaucetClaim(Faucet faucet, string cfduid, string phpsessid)
        {
            IRestResponse response;
            while (true)
            {
                var client = new RestClient("https://stakecube.net/app/community/functions")
                {
                    Timeout = 10000
                };
                var request = new RestRequest(Method.POST);
                request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
                request.AddHeader("Cookie", $"__cfduid={cfduid}; PHPSESSID={phpsessid}");
                request.AddParameter("ACTION", "CLAIM_FAUCET");
                request.AddParameter("ID", $"{faucet.ID}");
                response = client.Execute(request);
                if (!response.IsSuccessful)
                    continue;
                else
                    break;
            }
            return response;
        }

        private static Faucet NextClaimableFaucet(string cfduid, string phpsessid)
        {
            IRestResponse response = GetFaucets(cfduid, phpsessid);
            JsonSerializerSettings settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include
            };
            var dynamic = JsonConvert.DeserializeObject<dynamic>(response.Content, settings);

            foreach (var token in dynamic)
            {
                Faucet faucet = new Faucet();
                ConfigureFaucet(ref faucet, token);

                double balance;
                double amountPerClaim;
                if (decimalSeparator != ".")
                {
                    balance = Convert.ToDouble(faucet.BALANCE.Replace('.', ','));
                    amountPerClaim = Convert.ToDouble(faucet.AMOUNT_PER_CLAIM.Replace('.', ','));
                }
                else
                {
                    balance = Convert.ToDouble(faucet.BALANCE);
                    amountPerClaim = Convert.ToDouble(faucet.AMOUNT_PER_CLAIM);
                }

                if (amountPerClaim <= balance && (faucet.DIFF_IN_SEC == null || faucet.DIFF_IN_SEC >= 86400))
                {
                    return faucet;
                }
            }
            return null;
        }
    }
}
