
namespace ProtectBot
{
    public class Security
    {
        public string symbol { get; set; }
        public string baseAsset { get; set; }
        public string quoteAsset { get; set; }
        public string tickSize { get; set; }
        public string minQty { get; set; }
        public string stepSize { get; set; }
        public int precisPrice { get; set; }
        public int precisVolume { get; set; }
    }
}
