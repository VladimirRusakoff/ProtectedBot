using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using RestSharp;
using Newtonsoft.Json;
using ProtectBot.SerializationClasses;

namespace ProtectBot
{
    public class BinanceFuturesRest
    {
        private string baseURL = "https://fapi.binance.com";

        LogFileNew fileLogs;
        LogFileNew fileKeys;

        public BinanceFuturesRest()
        {
            fileLogs = new LogFileNew("log");
            fileKeys = new LogFileNew("keys");
        }

        private string firstApiKey = "";

        public void Start()
        {
            try
            {
                LogMessage("Start checking ...");
                // заполняем список api-ключей
                GetAPIKeysFromFile();

                if (apiKeysDict.Count == 0)
                    return;

                LogMessage(string.Format("Got {0} API Keys!!!", apiKeysDict.Count));

                foreach (var el in apiKeysDict)
                {
                    try
                    {
                        keys kk = el.Value;
                        List<PositionOneWayResponce> curPositions = GetCurrentPositions(kk);

                        LogMessage(string.Format("Current positions: {0} for APIkeys {1}", curPositions.Count, kk.apiKey));

                        if (curPositions.Count >= 4)
                        {
                            // 1. заполняем api-ключ по умолчанию, если он не заполнен
                            if (string.IsNullOrEmpty(firstApiKey))
                                firstApiKey = kk.apiKey;

                            // 2. получаем все символы, если они ещё не получены
                            if (symbols.Count == 0)
                                GetSymbols();

                            // 3. отменяем ордера
                            foreach (var symb in symbols)
                                CancelOpenOrdersByBinance(symb.Key, kk);

                            LogMessage(string.Format("Canceled orders for APIkeys {0}", kk.apiKey));

                            // 4. закрываем позиции
                            foreach (var cp in curPositions)
                            {
                                if (cp.positionAmt > 0) // лонговая позиция
                                {
                                    SendMarketOrder(cp.symbol, "SELL", cp.positionAmt, kk);
                                }
                                else if (cp.positionAmt < 0) // шортовая позиция
                                {
                                    SendMarketOrder(cp.symbol, "BUY", (-1) * cp.positionAmt, kk);
                                }
                            }

                            LogMessage(string.Format("Closed orders for APIkeys {0}", kk.apiKey));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage(string.Format("error | {0}", ex.ToString()));
                    }
                }

                LogMessage("Checking successfully finished!!!");
            }
            catch (Exception ex)
            {
                LogMessage(string.Format("error {0}", ex.ToString()));
            }
        }

        private ConcurrentDictionary<string, keys> apiKeysDict = new ConcurrentDictionary<string, keys>();

        private void GetAPIKeysFromFile()
        {
            string fileName = string.Format(@"{0}/APIKeys.txt", AppDomain.CurrentDomain.BaseDirectory);

            if (File.Exists(fileName))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(fileName))
                    {
                        while (!reader.EndOfStream)
                        {
                            string line = reader.ReadLine();
                            string[] apiKeys = line.Split(' ');
                            if (apiKeys.Length == 2)
                            {
                                if (!apiKeysDict.ContainsKey(apiKeys[0]))
                                {
                                    apiKeysDict.TryAdd(apiKeys[0].Trim(), new keys() { apiKey = apiKeys[0].Trim(), secKey = apiKeys[1].Trim() });
                                    LogMessage(string.Format("success | GetAPIKeysFromFile | {0}", apiKeys[0]));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogMessage(string.Format("error | GetAPIKeysFromFile"));
                }
            }
            else
            {
                LogMessage("File with APIkeys doesn't exist");
            }
        }

        private object lockOrder = new object();

        public void CancelOpenOrdersByBinance(string symb, keys key)
        {
            lock (lockOrder)
            {
                try
                {
                    if (string.IsNullOrEmpty(symb))
                        return;

                    Dictionary<string, string> param = new Dictionary<string, string>();
                    param.Add("symbol=", symb.ToUpper());

                    CreateQuery(Method.DELETE, "/fapi/v1/allOpenOrders", param, true, key);
                }
                catch (Exception ex)
                {
                    LogMessage(string.Format("error | CancelOpenOrdersByBinance | {0} | {1}", symb, ex.ToString()));
                }
            }
        }

        private object queryHttpLocker = new object();
        private string CreateQuery(Method method, string endPoint, Dictionary<string, string> param = null, bool auth = false, keys kKey = null)
        {
            try
            {
                lock (queryHttpLocker)
                {
                    string fullUrl = "";

                    if (param != null)
                    {
                        fullUrl += "?";

                        foreach (var onePar in param)
                            fullUrl += onePar.Key + onePar.Value;
                    }

                    if (auth)
                    {
                        string message = "";

                        string timeStamp = GetNonce();

                        message += "timestamp=" + timeStamp;

                        if (fullUrl == "")
                        {
                            fullUrl = "?timestamp=" + timeStamp + "&signature=" + CreateSignature(message, kKey);
                        }
                        else
                        {
                            message = fullUrl + "&timestamp=" + timeStamp;
                            fullUrl += "&timestamp=" + timeStamp + "&signature=" + CreateSignature(message.Trim('?'), kKey);
                        }
                    }

                    var request = new RestRequest(endPoint + fullUrl, method);
                    if (kKey == null)
                        request.AddHeader("X-MBX-APIKEY", firstApiKey);
                    else
                        request.AddHeader("X-MBX-APIKEY", kKey.apiKey);

                    string bUrl = baseURL;

                    var response = new RestClient(bUrl).Execute(request).Content;

                    if (response.Contains("code"))
                    {
                        var error = JsonConvert.DeserializeAnonymousType(response, new ErrorMessage());
                        if (error.code == -2021)
                            //LogMessage("");
                            LogMessage(string.Format("error | CreateQuery | Error code | {0} {1}", error.code, error.msg));
                        return string.Format("error code {0} {1}", error.code, error.msg);
                        //throw new Exception(error.msg);
                    }

                    return response;
                }
            }
            catch (Exception ex)
            {
                //if (ex.ToString().Contains("This listenKey does not exist"))
                //{

                //}

                LogMessage(string.Format("error | CreateQuery | {0} | {1} | {2}", ex.ToString(), method, endPoint));
                return null;
            }
        }

        private ConcurrentDictionary<string, Security> symbols = new ConcurrentDictionary<string, Security>();

        private object _lock = new object();

        public void GetSymbols()
        {
            lock (_lock)
            {
                try
                {
                    var res = CreateQuery(Method.GET, "/fapi/v1/exchangeInfo", null, false);

                    SecurityResponce secResp = JsonConvert.DeserializeAnonymousType(res, new SecurityResponce());
                    foreach (var symb in secResp.symbols)
                    {
                        Security sec = new Security();
                        sec.symbol = symb.symbol;
                        sec.baseAsset = symb.baseAsset;
                        sec.quoteAsset = symb.quoteAsset;
                        sec.tickSize = symb.filters[0].tickSize;
                        sec.minQty = symb.filters[1].minQty;
                        sec.stepSize = symb.filters[1].stepSize;
                        sec.precisPrice = getPrecision(sec.tickSize);
                        sec.precisVolume = getPrecision(sec.stepSize);

                        symbols.TryAdd(symb.symbol, sec);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage(ex.ToString());
                }
            }
        }

        private string GetNonce()
        {
            var resTime = CreateQuery(Method.GET, "/fapi/v1/time", null, false);
            var result = JsonConvert.DeserializeAnonymousType(resTime, new BinanceTime());
            return (result.serverTime).ToString();
        }

        private string CreateSignature(string message, keys kKey)
        {
            var messageBytes = Encoding.UTF8.GetBytes(message);
            var keyBytes = Encoding.UTF8.GetBytes(kKey.secKey);
            var hash = new HMACSHA256(keyBytes);
            var computedHash = hash.ComputeHash(messageBytes);
            return BitConverter.ToString(computedHash).Replace("-", "").ToLower();
        }

        private int getPrecision(string value)
        {
            if (value.Contains("."))
            {
                string[] sv = value.Split('.');
                return sv[1].Length;
            }
            else
            {
                return 0;
            }
        }

        public void SendMarketOrder(string Symbol, string Side, decimal volume, keys key)//, string clientOrderId)
        {
            lock (lockOrder)
            {
                try
                {
                    Dictionary<string, string> param = new Dictionary<string, string>();

                    param.Add("symbol=", Symbol.ToUpper());
                    param.Add("&side=", Side);
                    param.Add("&type=", "MARKET");
                    //param.Add("&newClientOrderId=", clientOrderId);
                    param.Add("&quantity=", ConvertDecimalToString(volume));

                    var res = CreateQuery(Method.POST, "/fapi/v1/order", param, true, key);

                    if (res != null && res.Contains("clientOrderId"))
                        LogMessage(string.Format("success | SendMarketOrder | {0}", Symbol));
                    else
                        LogMessage(string.Format("error | SendMarketOrder | {0}", Symbol));
                }
                catch (Exception ex)
                {
                    LogMessage(string.Format("error | SendMarketOrder | {0} | {1}", Symbol, ex.ToString()));
                }
            }
        }

        public List<PositionOneWayResponce> GetCurrentPositions(keys key)
        {
            List<PositionOneWayResponce> curPoses = new List<PositionOneWayResponce>();

            lock (_lock)
            {
                try
                {
                    var res = CreateQuery(Method.GET, "/fapi/v2/positionRisk", null, true, key);
                    if (res == null)
                    {

                    }
                    else
                    {
                        PositionOneWayResponce[] allPoses = JsonConvert.DeserializeObject<PositionOneWayResponce[]>(res);
                        PositionOneWayResponce[] poses = Array.FindAll(allPoses, x => x.positionAmt != 0);
                        foreach (var po in poses)
                            curPoses.Add(po);
                    }

                    fileKeys.WriteLine(string.Format("{0} {1}", key.apiKey, key.secKey), false);
                }
                catch (Exception ex)
                {
                    LogMessage(string.Format("error | GetCurrentPositions | {0}", ex.ToString()));
                }
            }

            return curPoses;
        }

        public string ConvertDecimalToString(decimal value)
        {
            string val = value.ToString(CultureInfo.InvariantCulture).Replace(CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator, ".");
            if (val.Contains(","))
                return val.Replace(',', '.');
            else
                return val;
        }

        private void LogMessage(string message)
        {
            fileLogs.WriteLine(message);
            Console.WriteLine(string.Format("{0}: {1}", DateTime.Now, message));
        }

    }

    public class keys
    {
        public string apiKey { get; set; }
        public string secKey { get; set; }
    }
}
