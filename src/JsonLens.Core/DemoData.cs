using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JsonLens.Core;

/// <summary>
/// Generates the large demo file (a fictional shop's order export) from a fixed seed, so it is
/// byte-for-byte the same every time and never needs to be committed. The default size is
/// about 37 MB and 1.37 million nodes.
/// </summary>
public static class DemoData
{
    /// <summary>Bump this when the generator changes so old cached files are regenerated.</summary>
    public const int Version = 1;
    public const ulong DefaultSeed = 20261123;
    public const int DefaultOrders = 40_000;

    public static string DefaultFileName => $"large-demo-v{Version}.json";

    /// <summary>Returns the path, generating the file first if it doesn't exist yet.</summary>
    public static string EnsureFile(string path, int orders = DefaultOrders, ulong seed = DefaultSeed)
    {
        if (File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            Write(fs, orders, seed);
        }
        File.Move(temp, path, overwrite: true);
        return path;
    }

    public static byte[] Generate(int orders, ulong seed = DefaultSeed)
    {
        using var ms = new MemoryStream();
        Write(ms, orders, seed);
        return ms.ToArray();
    }

    public static void Write(Stream stream, int orders = DefaultOrders, ulong seed = DefaultSeed)
    {
        var rng = new SplitMix64(seed);
        int customers = Math.Max(10, orders / 2);
        int products = Math.Clamp(orders / 100, 20, 400);
        var opts = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var w = new Utf8JsonWriter(stream, opts);

        var start = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string Date(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        w.WriteStartObject();
        w.WriteString("dataset", "Moosh Outfitters order export");
        w.WriteString("generated", "2026-01-01T00:00:00Z");
        w.WriteNumber("schemaVersion", 3);
        w.WriteNumber("seed", seed);
        w.WriteStartObject("counts");
        w.WriteNumber("stores", Stores.Length);
        w.WriteNumber("products", products);
        w.WriteNumber("customers", customers);
        w.WriteNumber("orders", orders);
        w.WriteEndObject();

        // Stores
        w.WriteStartArray("stores");
        for (int i = 0; i < Stores.Length; i++)
        {
            var (city, country) = Stores[i];
            w.WriteStartObject();
            w.WriteString("id", $"S{i + 1:D2}");
            w.WriteString("name", $"Moosh Outfitters {city}");
            w.WriteString("city", city);
            w.WriteString("country", country);
            w.WriteString("opened", Date(start.AddDays(-rng.Next(300, 3000))));
            w.WriteBoolean("flagship", i % 7 == 0);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // Products
        var productNames = new string[products];
        var productPrices = new decimal[products];
        w.WriteStartArray("products");
        for (int i = 0; i < products; i++)
        {
            string name = $"{Pick(rng, Adjectives)} {Pick(rng, Materials)} {Pick(rng, ProductNouns)}";
            decimal price = rng.Next(499, 39999) / 100m;
            productNames[i] = name;
            productPrices[i] = price;
            w.WriteStartObject();
            w.WriteString("sku", Sku(i));
            w.WriteString("name", name);
            w.WriteString("category", Pick(rng, Categories));
            w.WriteNumber("price", price);
            w.WriteNumber("stock", rng.Next(0, 900));
            w.WriteStartObject("dimensionsCm");
            w.WriteNumber("w", rng.Next(5, 120));
            w.WriteNumber("h", rng.Next(2, 80));
            w.WriteNumber("d", rng.Next(1, 60));
            w.WriteEndObject();
            w.WriteStartArray("ratings");
            int nr = rng.Next(0, 6);
            for (int r = 0; r < nr; r++) w.WriteNumberValue(rng.Next(1, 6));
            w.WriteEndArray();
            w.WriteBoolean("discontinued", rng.Next(0, 20) == 0);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // Customers
        w.WriteStartArray("customers");
        for (int i = 0; i < customers; i++)
        {
            string first = Pick(rng, FirstNames), last = Pick(rng, LastNames);
            var (city, country) = Pick(rng, Stores);
            w.WriteStartObject();
            w.WriteString("id", CustomerId(i));
            w.WriteString("name", $"{first} {last}");
            w.WriteString("email", $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}{i % 97}@example.com");
            w.WriteString("joined", Date(start.AddMinutes(rng.Next(0, 1_000_000))));
            int tier = rng.Next(0, 10);
            if (tier < 6) w.WriteNull("tier");
            else w.WriteString("tier", tier < 9 ? "silver" : "gold");
            w.WriteStartObject("address");
            w.WriteString("street", $"{rng.Next(1, 999)} {Pick(rng, Streets)}");
            w.WriteString("city", city);
            w.WriteString("country", country);
            w.WriteString("postcode", $"{rng.Next(10000, 99999)}");
            w.WriteEndObject();
            w.WriteStartArray("tags");
            int nt = rng.Next(0, 3);
            for (int t = 0; t < nt; t++) w.WriteStringValue(Pick(rng, CustomerTags));
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // Orders
        w.WriteStartArray("orders");
        for (int i = 0; i < orders; i++)
        {
            w.WriteStartObject();
            w.WriteNumber("id", 100001 + i);
            w.WriteString("customer", CustomerId(rng.Next(0, customers)));
            w.WriteString("store", $"S{rng.Next(1, Stores.Length + 1):D2}");
            var placed = start.AddMinutes(i * 26 + rng.Next(0, 25));
            w.WriteString("placed", Date(placed));
            string status = Pick(rng, Statuses);
            w.WriteString("status", status);

            int lines = 1 + rng.Next(0, 4);
            decimal total = 0;
            w.WriteStartArray("items");
            for (int l = 0; l < lines; l++)
            {
                int p = rng.Next(0, products);
                int qty = 1 + rng.Next(0, 3);
                total += productPrices[p] * qty;
                w.WriteStartObject();
                w.WriteString("sku", Sku(p));
                w.WriteString("name", productNames[p]);
                w.WriteNumber("qty", qty);
                w.WriteNumber("price", productPrices[p]);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("total", total);
            w.WriteString("currency", "USD");

            w.WriteStartObject("shipping");
            w.WriteString("method", Pick(rng, ShippingMethods));
            if (status is "shipped" or "delivered")
                w.WriteString("tracking", $"1Z{rng.Next(100000, 999999)}{rng.Next(1000, 9999)}");
            else
                w.WriteNull("tracking");
            w.WriteString("eta", Date(placed.AddDays(rng.Next(2, 9)).Date));
            w.WriteEndObject();

            if (rng.Next(0, 8) == 0) w.WriteString("note", Pick(rng, Notes));
            else w.WriteNull("note");
            w.WriteBoolean("gift", rng.Next(0, 10) == 0);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteEndObject();
        w.Flush();
    }

    private static string Sku(int i) => $"MO-{1000 + i * 7}";
    private static string CustomerId(int i) => $"C{i + 1:D6}";
    private static T Pick<T>(SplitMix64 rng, T[] items) => items[rng.Next(0, items.Length)];

    private static readonly (string City, string Country)[] Stores =
    {
        ("Halifax", "CA"), ("Toronto", "CA"), ("Vancouver", "CA"), ("Montréal", "CA"), ("Calgary", "CA"),
        ("Portland", "US"), ("Denver", "US"), ("Austin", "US"), ("Boston", "US"), ("Seattle", "US"),
        ("Reykjavík", "IS"), ("Oslo", "NO"), ("Tromsø", "NO"), ("Edinburgh", "GB"), ("Cork", "IE"),
        ("Zürich", "CH"), ("München", "DE"), ("Kraków", "PL"), ("Lyon", "FR"), ("Sapporo", "JP"),
        ("Queenstown", "NZ"), ("Hobart", "AU"), ("Ushuaia", "AR"), ("Anchorage", "US"),
    };

    private static readonly string[] Adjectives =
        { "Aurora", "Summit", "Tundra", "Glacier", "Ridge", "Coastal", "Boreal", "Alpine", "Drift", "Timber", "Polar", "Canyon" };
    private static readonly string[] Materials =
        { "Wool", "Down", "Fleece", "Canvas", "Merino", "Nylon", "Leather", "Waxed", "Gore", "Cotton" };
    private static readonly string[] ProductNouns =
        { "Parka", "Trail Jacket", "Beanie", "Mittens", "Boots", "Backpack", "Tent", "Sleeping Bag", "Thermos", "Hoodie", "Socks", "Snowshoes" };
    private static readonly string[] Categories =
        { "outerwear", "footwear", "accessories", "camping", "base layers" };
    private static readonly string[] FirstNames =
        { "Ava", "Liam", "Noor", "Mateo", "Freya", "Kenji", "Amara", "Oskar", "Lucía", "Ezra", "Ingrid", "Tariq", "Maple", "Rowan", "Saoirse", "Wren", "Yusuf", "Elodie", "Hamish", "Priya" };
    private static readonly string[] LastNames =
        { "MacLeod", "Okafor", "Lindqvist", "Tanaka", "Moreau", "Novak", "Haddad", "O'Brien", "Kowalski", "Sørensen", "Fraser", "Delacroix", "Mbeki", "Larsen", "Quinn" };
    private static readonly string[] Streets =
        { "Spruce St", "Harbour Rd", "Birch Ave", "Lighthouse Ln", "Moose Way", "Pine Cres", "Glacier Blvd", "Fjord Rd" };
    private static readonly string[] CustomerTags =
        { "newsletter", "wholesale", "returning", "early-access", "outdoor-club" };
    private static readonly string[] Statuses =
        { "delivered", "delivered", "delivered", "shipped", "shipped", "pending", "cancelled", "returned" };
    private static readonly string[] ShippingMethods =
        { "standard", "standard", "express", "pickup" };
    private static readonly string[] Notes =
        { "Leave at the back door", "Gift wrap please", "Call on arrival", "Fragile: thermos inside", "Deliver after 5pm", "Ring twice, the moose is friendly" };

    /// <summary>Small, fast, deterministic PRNG (SplitMix64) so output never depends on the runtime's Random.</summary>
    private sealed class SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed) => _state = seed;

        private ulong NextULong()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Integer in [min, max).</summary>
        public int Next(int min, int max) => min + (int)(NextULong() % (ulong)(max - min));
    }
}
