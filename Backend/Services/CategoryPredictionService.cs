public class CategoryPredictionService
{
    public string Predict(string description)
    {
        string text = description.ToLowerInvariant();

        if (text.Contains("swiggy") || text.Contains("zomato") || text.Contains("restaurant") || text.Contains("food"))
            return "Food";

        if (text.Contains("uber") || text.Contains("ola") || text.Contains("petrol") || text.Contains("fuel"))
            return "Transport";

        if (text.Contains("amazon") || text.Contains("flipkart") || text.Contains("myntra"))
            return "Shopping";

        if (text.Contains("recharge") || text.Contains("electricity") || text.Contains("bill") || text.Contains("wifi"))
            return "Utilities";

        if (text.Contains("salary") || text.Contains("credited") || text.Contains("received from"))
            return "Income";

        if (text.Contains("netflix") || text.Contains("spotify") || text.Contains("prime"))
            return "Entertainment";

        if (text.Contains("upi") || text.Contains("gpay") || text.Contains("phonepe"))
            return "UPI";

        return "Others";
    }
}