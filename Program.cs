using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace AdvancedInventorySystem
{
    class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Price { get; set; }
        public double SalePrice { get; set; }
        public int Stock { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft";
        public string ShortDescription { get; set; } = string.Empty;
        public string LongDescription { get; set; } = string.Empty;
        public List<string> Colors { get; set; } = new List<string>();
        public List<string> Sizes { get; set; } = new List<string>();
        public List<string> GalleryImages { get; set; } = new List<string>();
        public List<ColorVariant> ColorVariants { get; set; } = new List<ColorVariant>();
    }

    class ColorVariant
    {
        public string Name { get; set; } = string.Empty;
        public string HexCode { get; set; } = "#cccccc";
        public string ImageUrl { get; set; } = string.Empty;
    }

    class Program
    {
        private static string connString = "Host=localhost;Port=5432;Username=postgres;Password=1122;Database=InventoryManagementSystem";
        private static HttpListener listener = new HttpListener();

        static void Main(string[] args)
        {
            string url = "http://localhost:5100/";
            try
            {
                listener.Prefixes.Add(url);
                listener.Start();
                listener.BeginGetContext(new AsyncCallback(HandleIncomingConnections), null);
                
                Console.WriteLine("=========================================================");
                Console.WriteLine(" PRODUCT PORTAL ACTIVE!");
                Console.WriteLine($" Open Chrome and go to: {url}");
                Console.WriteLine("=========================================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Port binding failure: {ex.Message}");
            }

            Console.WriteLine("Press [Enter] at any time to shut down the server engine.");
            Console.ReadLine();
            listener.Stop();
        }

        private static void HandleIncomingConnections(IAsyncResult ar)
        {
            if (!listener.IsListening) return;

            try
            {
                var context = listener.EndGetContext(ar);
                listener.BeginGetContext(new AsyncCallback(HandleIncomingConnections), null);

                var request = context.Request;
                var response = context.Response;

                // Route 1: Get Products API
                if (request.Url?.AbsolutePath == "/api/products" && request.HttpMethod == "GET")
                {
                    response.ContentType = "application/json";
                    List<Product> catalog = FetchProductsFromDatabase();
                    string jsonString = JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true });
                    byte[] buffer = Encoding.UTF8.GetBytes(jsonString);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                // Route 2: Post New or Update Existing Product API
                else if (request.Url?.AbsolutePath == "/api/products" && request.HttpMethod == "POST")
                {
                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        string jsonBody = reader.ReadToEnd();
                        var incomingProduct = JsonSerializer.Deserialize<Product>(jsonBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        if (incomingProduct != null)
                        {
                            SaveProductToDatabase(incomingProduct);
                        }
                    }
                    response.StatusCode = (int)HttpStatusCode.OK;
                }
                // Route 2b: Delete Product API
                else if (request.Url?.AbsolutePath.StartsWith("/api/products/") == true && request.HttpMethod == "DELETE")
                {
                    string idString = request.Url.AbsolutePath.Replace("/api/products/", "");
                    if (int.TryParse(idString, out int targetId))
                    {
                        DeleteProductFromDatabase(targetId);
                        response.StatusCode = (int)HttpStatusCode.OK;
                    }
                    else
                    {
                        response.StatusCode = (int)HttpStatusCode.BadRequest;
                    }
                }
                // Dynamic Frontend View Routing Link
                else if (request.Url?.AbsolutePath.StartsWith("/products/") == true)
                {
                    response.ContentType = "text/html";
                    string dynamicId = request.Url.AbsolutePath.Replace("/products/", "");
                    
                    string viewHtml = "";
                    if (int.TryParse(dynamicId, out int pid))
                    {
                        Product? targetProduct = FetchSingleProductFromDatabase(pid);
                        if (targetProduct != null)
                        {
                            viewHtml = GetSingleProductPageHtml(targetProduct);
                        }
                        else
                        {
                            viewHtml = "<!DOCTYPE html><html><body class='p-12 font-sans'><h1>404 Product Not Found</h1><a href='/'>Back to Admin</a></body></html>";
                        }
                    }
                    
                    byte[] buffer = Encoding.UTF8.GetBytes(viewHtml);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                // Route 3: Render HTML Content Core Single-File Application Page
                else
                {
                    response.ContentType = "text/html";
                    string frontendHtml = GetFrontendHtml();
                    byte[] buffer = Encoding.UTF8.GetBytes(frontendHtml);
                    response.ContentLength64 = buffer.Length;
                    response.OutputStream.Write(buffer, 0, buffer.Length);
                }

                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Request Route Error]: {ex.Message}");
            }
        }

        private static List<Product> FetchProductsFromDatabase()
        {
            var list = new List<Product>();
            
            // Check if color_variants column exists, if not use fallback query
            string query = @"SELECT id, name, price, COALESCE(sale_price, 0), stock, COALESCE(sku, ''), COALESCE(status, 'Publish'), 
                             COALESCE(short_description, ''), COALESCE(long_description, ''), 
                             COALESCE(colors, '{}'::text[]), COALESCE(sizes, '{}'::text[]), COALESCE(gallery_images, '{}'::text[]),
                             COALESCE(color_variants, '[]'::jsonb)
                             FROM products ORDER BY id DESC";

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var product = new Product
                            {
                                Id = reader.GetInt32(0),
                                Name = reader.GetString(1),
                                Price = Convert.ToDouble(reader.GetDecimal(2)),
                                SalePrice = Convert.ToDouble(reader.GetDecimal(3)),
                                Stock = reader.GetInt32(4),
                                Sku = reader.GetString(5),
                                Status = reader.GetString(6),
                                ShortDescription = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                LongDescription = reader.IsDBNull(8) ? "" : reader.GetString(8)
                            };

                            // Handle colors - convert from PostgreSQL array to List<string>
                            if (!reader.IsDBNull(9))
                            {
                                var colorsArray = reader.GetValue(9) as string[];
                                product.Colors = colorsArray != null ? colorsArray.ToList() : new List<string>();
                            }
                            else
                            {
                                product.Colors = new List<string>();
                            }

                            // Handle sizes
                            if (!reader.IsDBNull(10))
                            {
                                var sizesArray = reader.GetValue(10) as string[];
                                product.Sizes = sizesArray != null ? sizesArray.ToList() : new List<string>();
                            }
                            else
                            {
                                product.Sizes = new List<string>();
                            }

                            // Handle gallery images
                            if (!reader.IsDBNull(11))
                            {
                                var galleryArray = reader.GetValue(11) as string[];
                                product.GalleryImages = galleryArray != null ? galleryArray.ToList() : new List<string>();
                            }
                            else
                            {
                                product.GalleryImages = new List<string>();
                            }

                            // Handle color variants (JSONB)
                            if (!reader.IsDBNull(12))
                            {
                                string jsonVariants = reader.GetString(12);
                                try
                                {
                                    product.ColorVariants = JsonSerializer.Deserialize<List<ColorVariant>>(jsonVariants) ?? new List<ColorVariant>();
                                }
                                catch
                                {
                                    product.ColorVariants = new List<ColorVariant>();
                                }
                            }
                            else
                            {
                                product.ColorVariants = new List<ColorVariant>();
                            }

                            list.Add(product);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database Retrieval Sync Error: {ex.Message}");
                // If color_variants column doesn't exist, try without it
                try
                {
                    string fallbackQuery = @"SELECT id, name, price, COALESCE(sale_price, 0), stock, COALESCE(sku, ''), COALESCE(status, 'Publish'), 
                                             COALESCE(short_description, ''), COALESCE(long_description, ''), 
                                             COALESCE(colors, '{}'::text[]), COALESCE(sizes, '{}'::text[]), COALESCE(gallery_images, '{}'::text[])
                                             FROM products ORDER BY id DESC";
                    
                    using (var conn = new NpgsqlConnection(connString))
                    {
                        conn.Open();
                        using (var cmd = new NpgsqlCommand(fallbackQuery, conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var product = new Product
                                {
                                    Id = reader.GetInt32(0),
                                    Name = reader.GetString(1),
                                    Price = Convert.ToDouble(reader.GetDecimal(2)),
                                    SalePrice = Convert.ToDouble(reader.GetDecimal(3)),
                                    Stock = reader.GetInt32(4),
                                    Sku = reader.GetString(5),
                                    Status = reader.GetString(6),
                                    ShortDescription = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                    LongDescription = reader.IsDBNull(8) ? "" : reader.GetString(8),
                                    Colors = reader.IsDBNull(9) ? new List<string>() : ((string[])reader.GetValue(9)).ToList(),
                                    Sizes = reader.IsDBNull(10) ? new List<string>() : ((string[])reader.GetValue(10)).ToList(),
                                    GalleryImages = reader.IsDBNull(11) ? new List<string>() : ((string[])reader.GetValue(11)).ToList(),
                                    ColorVariants = new List<ColorVariant>()
                                };
                                list.Add(product);
                            }
                        }
                    }
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"Fallback query also failed: {fallbackEx.Message}");
                }
            }
            return list;
        }

        private static Product? FetchSingleProductFromDatabase(int id)
        {
            string query = @"SELECT id, name, price, COALESCE(sale_price, 0), stock, COALESCE(sku, ''), COALESCE(status, 'Publish'), 
                             COALESCE(short_description, ''), COALESCE(long_description, ''), 
                             COALESCE(colors, '{}'::text[]), COALESCE(sizes, '{}'::text[]), COALESCE(gallery_images, '{}'::text[]),
                             COALESCE(color_variants, '[]'::jsonb)
                             FROM products WHERE id = @id";
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("id", id);
                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                var product = new Product
                                {
                                    Id = reader.GetInt32(0),
                                    Name = reader.GetString(1),
                                    Price = Convert.ToDouble(reader.GetDecimal(2)),
                                    SalePrice = Convert.ToDouble(reader.GetDecimal(3)),
                                    Stock = reader.GetInt32(4),
                                    Sku = reader.GetString(5),
                                    Status = reader.GetString(6),
                                    ShortDescription = reader.IsDBNull(7) ? "" : reader.GetString(7),
                                    LongDescription = reader.IsDBNull(8) ? "" : reader.GetString(8),
                                    Colors = reader.IsDBNull(9) ? new List<string>() : ((string[])reader.GetValue(9)).ToList(),
                                    Sizes = reader.IsDBNull(10) ? new List<string>() : ((string[])reader.GetValue(10)).ToList(),
                                    GalleryImages = reader.IsDBNull(11) ? new List<string>() : ((string[])reader.GetValue(11)).ToList()
                                };

                                if (!reader.IsDBNull(12))
                                {
                                    string jsonVariants = reader.GetString(12);
                                    try
                                    {
                                        product.ColorVariants = JsonSerializer.Deserialize<List<ColorVariant>>(jsonVariants) ?? new List<ColorVariant>();
                                    }
                                    catch
                                    {
                                        product.ColorVariants = new List<ColorVariant>();
                                    }
                                }

                                return product;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database Single Item Fetch Error: {ex.Message}");
            }
            return null;
        }

        private static void SaveProductToDatabase(Product p)
        {
            string query;
            if (p.Id > 0)
            {
                query = @"UPDATE products SET name=@name, price=@price, sale_price=@sale_price, stock=@stock, sku=@sku, 
                          status=@status, short_description=@desc, long_description=@long_desc, colors=@colors, sizes=@sizes, 
                          gallery_images=@gallery, color_variants=@color_variants::jsonb
                          WHERE id=@id";
            }
            else
            {
                query = @"INSERT INTO products (name, price, sale_price, stock, sku, status, short_description, long_description, colors, sizes, gallery_images, color_variants) 
                          VALUES (@name, @price, @sale_price, @stock, @sku, @status, @desc, @long_desc, @colors, @sizes, @gallery, @color_variants::jsonb)";
            }

            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        if (p.Id > 0) cmd.Parameters.AddWithValue("id", p.Id);
                        cmd.Parameters.AddWithValue("name", p.Name);
                        cmd.Parameters.AddWithValue("price", Convert.ToDecimal(p.Price));
                        cmd.Parameters.AddWithValue("sale_price", Convert.ToDecimal(p.SalePrice));
                        cmd.Parameters.AddWithValue("stock", p.Stock);
                        cmd.Parameters.AddWithValue("sku", p.Sku ?? string.Empty);
                        cmd.Parameters.AddWithValue("status", p.Status ?? "Publish");
                        cmd.Parameters.AddWithValue("desc", p.ShortDescription ?? string.Empty);
                        cmd.Parameters.AddWithValue("long_desc", p.LongDescription ?? string.Empty);
                        cmd.Parameters.AddWithValue("colors", p.Colors.ToArray());
                        cmd.Parameters.AddWithValue("sizes", p.Sizes.ToArray());
                        cmd.Parameters.AddWithValue("gallery", p.GalleryImages.ToArray());
                        
                        string colorVariantsJson = JsonSerializer.Serialize(p.ColorVariants ?? new List<ColorVariant>());
                        cmd.Parameters.AddWithValue("color_variants", colorVariantsJson);
                        
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"PostgreSQL Write Error Execution: {ex.Message}");
            }
        }

        private static void DeleteProductFromDatabase(int id)
        {
            string query = "DELETE FROM products WHERE id = @id";
            try
            {
                using (var conn = new NpgsqlConnection(connString))
                {
                    conn.Open();
                    using (var cmd = new NpgsqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PostgreSQL Delete Error Execution: {ex.Message}");
            }
        }

        private static string GetSingleProductPageHtml(Product p)
        {
            string mainFallbackImage = p.GalleryImages.Count > 0 ? p.GalleryImages[0] : "https://images.unsplash.com/photo-1616627561950-9f746e330187?w=600&q=80";
            
            string thumbnailHtmlList = string.Join("", p.GalleryImages.Select((img, index) => $@"
                <button onclick=""document.getElementById('primaryStageWindow').src='{img}'"" class='w-16 h-16 border border-gray-200 hover:border-gray-900 rounded bg-white overflow-hidden p-0.5 transition cursor-pointer shadow-3xs'>
                    <img src='{img}' class='w-full h-full object-cover' alt='Thumbnail {index + 1}'>
                </button>"));

            string colorBlocksHtml = string.Join("", p.ColorVariants.Select(variant => $@"
                <div class='flex flex-col items-center gap-1'>
                    <button onclick=""document.getElementById('primaryStageWindow').src='{variant.ImageUrl}'"" 
                            class='w-10 h-10 border-2 border-gray-300 hover:border-gray-900 rounded-full relative group cursor-pointer shadow-3xs hover:scale-110 transition duration-100 shrink-0' 
                            title='{variant.Name}'
                            style='background-color: {variant.HexCode};'>
                        {(!string.IsNullOrEmpty(variant.ImageUrl) ? $"<img src='{variant.ImageUrl}' class='w-full h-full rounded-full object-cover' alt='{variant.Name}'>" : "")}
                        <span class='absolute bottom-full left-1/2 -translate-x-1/2 mb-1.5 bg-gray-900 text-white text-[10px] py-0.5 px-1.5 rounded opacity-0 group-hover:opacity-100 pointer-events-none transition whitespace-nowrap z-10'>{variant.Name}</span>
                    </button>
                    <span class='text-[8px] text-gray-500 uppercase tracking-wider'>{variant.Name}</span>
                </div>"));

            double basePriceExcVat = p.Price;
            double priceWithVat = basePriceExcVat * 1.20;

            return $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>{p.Name} - Store Portal</title>
    <script src='https://cdn.jsdelivr.net/npm/@tailwindcss/browser@4'></script>
    <link href='https://fonts.googleapis.com/css2?family=Open+Sans:wght@400;600;700&display=swap' rel='stylesheet'>
    <style>
        body {{ font-family: 'Open Sans', sans-serif; }}
    </style>
</head>
<body class='bg-white text-[#2c3338] antialiased min-h-screen px-4 py-6 sm:px-16 sm:py-10 select-none'>
    
    <div class='max-w-6xl mx-auto'>
        <nav class='text-[10px] sm:text-xs font-semibold uppercase tracking-widest text-[#8c8f94] mb-8 flex flex-wrap items-center gap-1.5 border-b border-gray-100 pb-3'>
            <a href='/' class='hover:text-blue-600 transition'>Home</a> <span class='text-gray-300'>/</span>
            <a href='#' class='hover:text-blue-600 transition'>Shop</a> <span class='text-gray-300'>/</span>
            <span class='text-gray-800 font-bold'>{p.Name.ToUpper()}</span>
        </nav>

        <div class='grid grid-cols-1 md:grid-cols-12 gap-10 md:gap-14 items-start'>
            
            <div class='md:col-span-5 space-y-4 sticky top-6'>
                <div class='border border-gray-200 bg-[#fbfbfb] rounded-sm relative overflow-hidden flex items-center justify-center p-2 group shadow-2xs aspect-square'>
                    <img id='primaryStageWindow' src='{mainFallbackImage}' class='max-h-full max-w-full object-contain transition-all duration-300' alt='Primary Dynamic Asset View'>
                    <button class='absolute bottom-3 left-3 bg-white hover:bg-gray-100 rounded-full w-8 h-8 flex items-center justify-center border border-gray-200 shadow-sm transition cursor-pointer text-xs' onclick='alert(""Magnifier canvas modal trigger integration context."")'>
                        🔍
                    </button>
                </div>
                
                <div class='flex flex-wrap gap-2 pt-1'>
                    {thumbnailHtmlList}
                </div>
            </div>

            <div class='md:col-span-7 space-y-6'>
                <div>
                    <h1 class='text-2xl sm:text-4xl font-bold tracking-tight text-[#1d2327] font-sans'>{p.Name}</h1>
                    <div class='w-12 h-[3px] bg-gray-100 mt-5 mb-3'></div>
                </div>

                <div class='text-xl sm:text-2xl text-gray-900 font-bold tracking-tight flex flex-wrap items-baseline gap-1.5'>
                    <span>${basePriceExcVat:F2}</span>
                    <span class='text-xs sm:text-sm text-gray-400 font-normal tracking-wide'>exc. VAT (${priceWithVat:F2} inc. VAT)</span>
                </div>

                <div class='text-sm sm:text-base text-gray-600 leading-relaxed font-medium pt-2 border-t border-gray-50'>
                    <span class='font-bold text-gray-900 block mb-1'>Fabric parameter scope specification context:</span>
                    <p class='italic text-gray-500 font-normal text-sm'>{p.ShortDescription}</p>
                </div>

                {(p.ColorVariants.Count > 0 ? $@"
                <div class='space-y-3 pt-4 border-t border-gray-100'>
                    <span class='block text-[11px] font-bold uppercase tracking-widest text-gray-400'>Select Color Variant</span>
                    <div class='flex flex-wrap gap-4'>{colorBlocksHtml}</div>
                </div>" : "")}

                <div class='pt-6 border-t border-gray-100 flex items-center gap-4 max-w-md'>
                    <div class='flex items-center border border-gray-300 bg-gray-50 h-10 overflow-hidden shrink-0 rounded-sm'>
                        <button onclick='let e=document.getElementById(""qtyCounter""); if(parseInt(e.value)>1)e.value=parseInt(e.value)-1' class='w-8 text-center text-base font-bold hover:bg-gray-200 h-full cursor-pointer transition select-none border-r border-gray-200'>-</button>
                        <input type='text' id='qtyCounter' value='1' class='w-9 text-center text-xs font-bold border-none bg-transparent focus:outline-none text-[#1d2327]' readonly>
                        <button onclick='let e=document.getElementById(""qtyCounter""); e.value=parseInt(e.value)+1' class='w-8 text-center text-base font-bold hover:bg-gray-200 h-full cursor-pointer transition select-none border-l border-gray-200'>+</button>
                    </div>
                    
                    <button onclick='alert(""Active tracking metrics unified to shopping baseline pipeline successfully."")' class='flex-1 bg-[#1d2327] hover:bg-emerald-600 active:bg-emerald-700 text-white font-bold h-10 px-6 rounded-sm text-xs uppercase tracking-widest transition-all duration-150 cursor-pointer text-center shadow-xs'>
                        Add to Basket
                    </button>
                </div>

                <div class='pt-5 border-t border-gray-100 space-y-1.5 text-xs text-gray-400 font-medium tracking-wide'>
                    <div>SKU Identification Track: <span class='text-gray-700 font-mono font-bold'>{(!string.IsNullOrEmpty(p.Sku) ? p.Sku : "N/A")}</span></div>
                    <div>Inventory Status Level: <span class='{(p.Stock <= 5 ? "text-rose-600" : "text-emerald-600")} font-bold'>{p.Stock} items remaining</span></div>
                </div>
            </div>

            {(!string.IsNullOrEmpty(p.LongDescription) ? $@"
            <div class='md:col-span-12 mt-8 border-t border-gray-200 pt-6'>
                <h3 class='text-[10px] sm:text-xs font-bold uppercase tracking-widest text-gray-400 mb-3'>Detailed Technical Parameter Scope</h3>
                <div class='text-xs sm:text-sm text-gray-600 leading-relaxed whitespace-pre-line bg-gray-50 rounded border border-gray-100 p-5 font-mono shadow-3xs text-justify'>{p.LongDescription}</div>
            </div>" : "")}

        </div>
    </div>

</body>
</html>";
        }

        private static string GetFrontendHtml()
        {
            return @"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Product Management System</title>
    <script src='https://cdn.jsdelivr.net/npm/@tailwindcss/browser@4'></script>
    <link href='https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.4.0/css/all.min.css' rel='stylesheet'>
    <style>
        .color-variant-card {
            transition: all 0.2s ease;
        }
        .color-variant-card:hover {
            transform: translateY(-2px);
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }
        .color-picker-wrapper {
            position: relative;
            width: 40px;
            height: 40px;
            overflow: hidden;
            border-radius: 50%;
            border: 2px solid #e5e7eb;
            cursor: pointer;
        }
        .color-picker-wrapper input[type='color'] {
            position: absolute;
            top: -10px;
            left: -10px;
            width: 60px;
            height: 60px;
            cursor: pointer;
            border: none;
            padding: 0;
        }
        .color-thumbnail {
            width: 40px;
            height: 40px;
            border-radius: 50%;
            border: 2px solid #e5e7eb;
            background-size: cover;
            background-position: center;
        }
        .table-row-enter {
            animation: fadeIn 0.3s ease-in;
        }
        @keyframes fadeIn {
            from { opacity: 0; transform: translateY(-10px); }
            to { opacity: 1; transform: translateY(0); }
        }
        .toast {
            position: fixed;
            bottom: 20px;
            right: 20px;
            z-index: 9999;
            animation: slideIn 0.3s ease-out;
        }
        @keyframes slideIn {
            from { transform: translateX(100%); opacity: 0; }
            to { transform: translateX(0); opacity: 1; }
        }
    </style>
</head>
<body class='bg-[#f0f0f1] text-[#2c3338] font-sans min-h-screen pb-16'>

    <header class='bg-[#1d2327] text-white shadow-md sticky top-0 z-50 h-14 flex items-center px-6 justify-between'>
        <div class='flex items-center gap-3'>
            <i class='fas fa-boxes text-emerald-400 text-xl'></i>
            <span class='text-sm font-bold tracking-wide'>Product Management System</span>
        </div>
        <div class='flex items-center gap-3 text-xs'>
            <span class='text-gray-400'>📦 Database Connected</span>
            <span class='w-2 h-2 bg-emerald-400 rounded-full inline-block'></span>
        </div>
    </header>

    <main class='max-w-[1400px] mx-auto px-4 mt-6 grid grid-cols-1 xl:grid-cols-12 gap-6'>
        
        <section class='xl:col-span-8 space-y-6'>
            <div class='bg-white p-6 rounded-lg border border-[#ccd0d4] shadow-sm'>
                <h1 id='formPanelTitle' class='text-lg font-semibold mb-5 text-[#1d2327] flex items-center gap-2'>
                    <i class='fas fa-plus-circle text-emerald-500'></i> Add New Product
                </h1>
                <input type='hidden' id='pId' value='0'>
                <div class='space-y-5'>
                    <div>
                        <label class='block text-xs font-bold text-[#1d2327] mb-1.5 uppercase tracking-wide'>Product Name <span class='text-red-500'>*</span></label>
                        <input type='text' id='pName' required class='w-full px-4 py-2.5 text-base border border-[#8c8f94] rounded-md focus:outline-none focus:border-[#2271b1] focus:ring-1 focus:ring-[#2271b1] transition' placeholder='Enter product title...'>
                    </div>
                    <div>
                        <label class='block text-xs font-bold text-[#1d2327] mb-1.5 uppercase tracking-wide'>Product Long Description / Specs</label>
                        <textarea id='pLongDesc' rows='5' class='w-full px-4 py-2.5 border border-[#8c8f94] rounded-md text-sm focus:outline-none focus:border-[#2271b1]' placeholder='Write detailed product descriptions...'></textarea>
                    </div>
                </div>
            </div>

            <div class='bg-white rounded-lg border border-[#ccd0d4] shadow-sm overflow-hidden'>
                <div class='bg-[#f6f7f7] border-b border-[#ccd0d4] px-5 py-3 flex items-center justify-between'>
                    <span class='text-sm font-semibold text-[#1d2327]'>Product Data — <span class='text-blue-700 font-normal text-xs'>Variable Schema</span></span>
                </div>
                
                <div class='grid grid-cols-1 md:grid-cols-12 min-h-[320px]'>
                    <div class='md:col-span-3 bg-[#f6f7f7] border-r border-[#ccd0d4] flex flex-col text-xs font-medium text-[#2c3338]'>
                        <button class='px-4 py-3 text-left bg-white border-r-4 border-emerald-500 font-bold text-[#1d2327] flex items-center gap-2'>
                            <i class='fas fa-dollar-sign'></i> General Pricing
                        </button>
                        <button class='px-4 py-3 text-left border-b border-slate-200/40 hover:bg-slate-100/60 flex items-center gap-2' onclick='alert(""Unified Inventory Panel Overview Mode."")'>
                            <i class='fas fa-box'></i> Inventory Configuration
                        </button>
                        <button class='px-4 py-3 text-left border-b border-slate-200/40 hover:bg-slate-100/60 flex items-center gap-2' onclick='alert(""Split comma variation token parsing strategy configured below."")'>
                            <i class='fas fa-palette'></i> Product Attributes
                        </button>
                    </div>

                    <div class='md:col-span-9 p-6 space-y-5 text-sm'>
                        <div class='grid grid-cols-1 sm:grid-cols-2 gap-4 border-b border-slate-100 pb-4'>
                            <div>
                                <label class='block text-xs font-semibold text-slate-500 mb-1'>Regular Price ($)</label>
                                <input type='number' step='0.01' id='pPrice' required class='w-full px-3 py-2 border border-[#8c8f94] rounded-md text-sm focus:outline-none focus:border-[#2271b1]' placeholder='0.00'>
                            </div>
                            <div>
                                <label class='block text-xs font-semibold text-slate-500 mb-1'>Sale Price ($)</label>
                                <input type='number' step='0.01' id='pSalePrice' class='w-full px-3 py-2 border border-[#8c8f94] rounded-md text-sm focus:outline-none focus:border-[#2271b1]' placeholder='Optional sales price'>
                            </div>
                        </div>

                        <div class='grid grid-cols-1 sm:grid-cols-2 gap-4 border-b border-slate-100 pb-4'>
                            <div>
                                <label class='block text-xs font-semibold text-slate-500 mb-1'>SKU Identification String</label>
                                <input type='text' id='pSku' class='w-full px-3 py-2 border border-[#8c8f94] rounded-md text-sm focus:outline-none focus:border-[#2271b1]' placeholder='e.g., IVY-LEAF-TAB-01'>
                            </div>
                            <div>
                                <label class='block text-xs font-semibold text-slate-500 mb-1'>Stock Inventory</label>
                                <input type='number' id='pStock' required class='w-full px-3 py-2 border border-[#8c8f94] rounded-md text-sm focus:outline-none focus:border-[#2271b1]' placeholder='100'>
                            </div>
                        </div>

                        <div class='space-y-4'>
                            <div>
                                <label class='block text-xs font-bold text-slate-700 mb-2 flex items-center justify-between'>
                                    <span><i class='fas fa-palette mr-1'></i> Color Variants with Shades & Images</span>
                                    <button type='button' onclick='addColorVariant()' class='bg-emerald-500 hover:bg-emerald-600 text-white px-3 py-1.5 rounded-md text-xs font-bold transition shadow-sm'>
                                        <i class='fas fa-plus mr-1'></i> Add Color
                                    </button>
                                </label>
                                <div id='colorVariantsContainer' class='space-y-3 mt-2'></div>
                            </div>
                            <div>
                                <label class='block text-xs font-bold text-slate-700 mb-1 flex items-center justify-between'>
                                    <span><i class='fas fa-arrows-alt-h mr-1'></i> Size Configurations</span>
                                    <span class='text-[10px] font-normal text-slate-400'>(Comma separated list)</span>
                                </label>
                                <input type='text' id='pSizes' class='w-full px-3 py-2 border border-[#8c8f94] rounded-md text-sm focus:outline-none focus:border-[#2271b1]' placeholder='70x70cm, 130x170cm, 200x200cm'>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <div class='bg-white p-5 rounded-lg border border-[#ccd0d4] shadow-sm'>
                <label class='block text-xs font-bold text-[#1d2327] mb-1.5 uppercase tracking-wide'>Product Short Description Excerpt</label>
                <textarea id='pShortDesc' rows='2' class='w-full px-4 py-2.5 border border-[#8c8f94] rounded-md text-sm focus:outline-none focus:border-[#2271b1]' placeholder='Short summary excerpt viewed on modern shop index pages...'></textarea>
            </div>
        </section>

        <section class='xl:col-span-4 space-y-6'>
            <div class='bg-white rounded-lg border border-[#ccd0d4] shadow-sm overflow-hidden'>
                <div class='bg-[#f6f7f7] border-b border-[#ccd0d4] px-4 py-3 font-semibold text-sm text-[#1d2327] flex items-center gap-2'>
                    <i class='fas fa-rocket text-emerald-500'></i> Publish Settings
                </div>
                <div class='p-4 space-y-4 text-xs text-[#2c3338]'>
                    <div class='flex items-center justify-between'>
                        <span class='font-medium text-slate-500'>Catalogue Sync Status:</span>
                        <select id='pStatus' class='border border-[#8c8f94] px-3 py-1.5 rounded-md bg-white text-xs cursor-pointer focus:outline-none focus:border-[#2271b1]'>
                            <option value='Publish'>🚀 Publish Live</option>
                            <option value='Draft'>📝 Draft State</option>
                            <option value='Pending'>⏳ Pending Review</option>
                        </select>
                    </div>
                    <div class='border-t border-slate-100 pt-4 flex flex-col gap-2'>
                        <button id='submitFormBtn' onclick='submitProductForm(event)' type='button' class='w-full bg-[#2271b1] hover:bg-[#135e96] border-b-2 border-[#0a4b78] text-white font-semibold py-2.5 px-4 rounded-md cursor-pointer transition text-center shadow-sm text-sm flex items-center justify-center gap-2'>
                            <i class='fas fa-save'></i> Save & Publish Product
                        </button>
                        <button id='cancelEditBtn' onclick='resetApplicationFormState()' type='button' class='hidden w-full bg-slate-100 hover:bg-slate-200 text-slate-700 font-semibold py-2 px-4 rounded-md border border-slate-300 transition text-center text-xs flex items-center justify-center gap-2'>
                            <i class='fas fa-times'></i> Cancel Edit Mode
                        </button>
                    </div>
                </div>
            </div>

            <div class='bg-white rounded-lg border border-[#ccd0d4] shadow-sm overflow-hidden'>
                <div class='bg-[#f6f7f7] border-b border-[#ccd0d4] px-4 py-3 font-semibold text-sm text-[#1d2327] flex items-center gap-2'>
                    <i class='fas fa-images'></i> Gallery Images
                </div>
                <div class='p-4 space-y-3'>
                    <div class='flex items-center gap-2'>
                        <input type='file' id='pGalleryFiles' multiple accept='image/*' class='hidden' onchange='handleGalleryUpload(this)'>
                        <button type='button' onclick='document.getElementById(""pGalleryFiles"").click()' class='bg-[#f6f7f7] border border-[#ccd0d4] text-[#2c3338] px-4 py-2 rounded-md text-xs font-semibold hover:bg-slate-100 cursor-pointer shadow-sm transition flex items-center gap-2'>
                            <i class='fas fa-upload'></i> Upload Images
                        </button>
                        <span class='text-[10px] text-gray-400'>Select multiple files</span>
                    </div>
                    <div id='galleryPreview' class='flex flex-wrap gap-2 min-h-[60px] p-3 bg-gray-50 rounded-lg border border-dashed border-gray-200'></div>
                </div>
            </div>
        </section>

        <section class='xl:col-span-12 mt-6 space-y-4'>
            <div class='flex items-center justify-between px-1'>
                <h3 class='text-sm font-bold text-[#1d2327] uppercase tracking-wider flex items-center gap-2'>
                    <i class='fas fa-database text-emerald-500'></i> Live Database Storage Track
                </h3>
                <span id='catalogBadgeCount' class='bg-[#1d2327] text-white text-[11px] px-3 py-1 rounded-full font-bold shadow-sm'>0 Items</span>
            </div>
            
            <div class='bg-white rounded-lg border border-[#ccd0d4] shadow-sm overflow-hidden'>
                <div class='overflow-x-auto w-full'>
                    <table class='w-full text-left text-xs border-collapse'>
                        <thead>
                            <tr class='bg-[#f6f7f7] border-b border-[#ccd0d4] font-semibold text-[#1d2327]'>
                                <th class='p-3 w-16'>ID</th>
                                <th class='p-3 w-32'>SKU</th>
                                <th class='p-3'>Product Name</th>
                                <th class='p-3 w-28'>Price</th>
                                <th class='p-3 w-24'>Stock</th>
                                <th class='p-3 w-24'>Status</th>
                                <th class='p-3 w-56 text-center'>Actions</th>
                            </tr>
                        </thead>
                        <tbody id='realtimeCatalogTableBody' class='divide-y divide-[#ccd0d4]/60'></tbody>
                    </table>
                </div>
                <div id='tableEmptyStateMessage' class='p-10 text-center text-xs text-slate-400 font-medium bg-slate-50/50'>
                    <i class='fas fa-inbox text-4xl text-slate-300 block mb-3'></i>
                    No dynamic database entries mapped in permanent SQL database logs.
                </div>
            </div>
        </section>
    </main>

    <!-- Toast Notification -->
    <div id='toast' class='toast hidden bg-[#1d2327] text-white px-6 py-3 rounded-lg shadow-lg text-sm font-medium flex items-center gap-3'>
        <i class='fas fa-check-circle text-emerald-400'></i>
        <span id='toastMessage'>Product saved successfully!</span>
    </div>

    <script>
        let localInMemoryCache = [];
        let currentGalleryBase64 = [];
        let colorVariants = [];

        const colorMap = {
            'airforce': '#5d8aa8', 'apple green': '#8db600', 'black': '#111111', 'burgundy': '#800020',
            'chocolate': '#7b3f00', 'daffodil': '#ffff31', 'dark grey': '#555555', 'dark lilac': '#9c7c9c',
            'forest green': '#228b22', 'gold': '#ffd700', 'ivory': '#fffff0', 'light grey': '#d3d3d3',
            'lime green': '#32cd32', 'navy blue': '#000080', 'pink': '#ffc0cb', 'purple': '#800080',
            'red': '#e50000', 'royal blue': '#4169e1', 'sandalwood': '#c2b280', 'seafoam': '#9fe2bf',
            'tango orange': '#f94d00', 'terracotta': '#e2725b', 'turquoise': '#40e0d0', 'wedgewood': '#5684b5',
            'white': '#ffffff', 'aubergine': '#3d0734', 'olive green': '#808000', 'sage green': '#87a96b',
            'cream white': '#fffdd0', 'coral': '#ff7f50', 'indigo': '#4b0082', 'lavender': '#e6e6fa',
            'maroon': '#800000', 'orchid': '#da70d6', 'plum': '#dda0dd', 'salmon': '#fa8072',
            'silver': '#c0c0c0', 'tan': '#d2b48c', 'violet': '#ee82ee', 'wheat': '#f5deb3'
        };

        function getColorHex(name) {
            const clean = name.trim().toLowerCase();
            return colorMap[clean] || '#cccccc';
        }

        function addColorVariant(name = '', hexCode = '', imageUrl = '') {
            const container = document.getElementById('colorVariantsContainer');
            const id = Date.now() + Math.random();
            
            const card = document.createElement('div');
            card.className = 'color-variant-card bg-gray-50 border border-gray-200 rounded-lg p-3 flex items-center gap-3';
            card.id = `color-${id}`;
            card.innerHTML = `
                <div class='flex-1 grid grid-cols-1 sm:grid-cols-4 gap-2'>
                    <input type='text' class='color-name-input px-3 py-2 border border-gray-300 rounded-md text-xs w-full' placeholder='Color name' value='${name}'>
                    <div class='flex items-center gap-2'>
                        <div class='color-picker-wrapper'>
                            <input type='color' class='color-hex-input' value='${hexCode || '#cccccc'}' onchange='updateColorPreview(this)'>
                            <div class='color-preview absolute inset-0 pointer-events-none' style='background-color: ${hexCode || '#cccccc'}; border-radius: 50%;'></div>
                        </div>
                        <span class='color-hex-text text-xs font-mono text-gray-500'>${hexCode || '#cccccc'}</span>
                    </div>
                    <div class='flex items-center gap-2'>
                        <input type='file' class='color-image-input hidden' accept='image/*' onchange='handleColorImageUpload(this, ${id})'>
                        <button onclick='this.previousElementSibling.click()' class='bg-blue-50 hover:bg-blue-100 text-blue-600 px-3 py-1.5 rounded-md text-xs transition border border-blue-200 flex items-center gap-1'>
                            <i class='fas fa-image'></i> Upload
                        </button>
                        ${imageUrl ? `<img src='${imageUrl}' class='color-thumbnail w-8 h-8 rounded-full object-cover' alt='Color'>` : ''}
                        <span class='color-image-status text-xs text-gray-400'>${imageUrl ? '✅' : ''}</span>
                    </div>
                </div>
                <button onclick='removeColorVariant(${id})' class='text-red-400 hover:text-red-600 transition px-2'>
                    <i class='fas fa-times'></i>
                </button>
            `;
            
            container.appendChild(card);
            updateColorVariantsData();
        }

        function removeColorVariant(id) {
            const element = document.getElementById(`color-${id}`);
            if (element) {
                element.remove();
                updateColorVariantsData();
            }
        }

        function updateColorPreview(input) {
            const parent = input.closest('.flex');
            const preview = parent.querySelector('.color-preview');
            const hexText = parent.querySelector('.color-hex-text');
            const color = input.value;
            if (preview) preview.style.backgroundColor = color;
            if (hexText) hexText.textContent = color;
            updateColorVariantsData();
        }

        function handleColorImageUpload(input, id) {
            const file = input.files[0];
            if (!file) return;
            
            const reader = new FileReader();
            reader.onload = (e) => {
                const card = document.getElementById(`color-${id}`);
                const imageData = e.target.result;
                
                const img = card.querySelector('.color-thumbnail') || document.createElement('img');
                img.className = 'color-thumbnail w-8 h-8 rounded-full object-cover';
                img.src = imageData;
                
                const imageSection = card.querySelector('.flex.items-center.gap-2:last-child');
                const statusSpan = imageSection.querySelector('.color-image-status');
                if (statusSpan) statusSpan.textContent = '✅';
                
                const existingImg = imageSection.querySelector('.color-thumbnail');
                if (existingImg) existingImg.remove();
                
                const buttons = imageSection.querySelectorAll('button');
                if (buttons.length > 0) {
                    buttons[buttons.length - 1].after(img);
                } else {
                    imageSection.appendChild(img);
                }
                
                updateColorVariantsData();
            };
            reader.readAsDataURL(file);
        }

        function updateColorVariantsData() {
            const cards = document.querySelectorAll('.color-variant-card');
            colorVariants = [];
            
            cards.forEach(card => {
                const nameInput = card.querySelector('.color-name-input');
                const colorInput = card.querySelector('.color-hex-input');
                const img = card.querySelector('.color-thumbnail');
                
                const name = nameInput ? nameInput.value.trim() : '';
                const hexCode = colorInput ? colorInput.value : '#cccccc';
                const imageUrl = img ? img.src : '';
                
                if (name) {
                    colorVariants.push({
                        name: name,
                        hexCode: hexCode,
                        imageUrl: imageUrl
                    });
                }
            });
        }

        document.addEventListener('input', function(e) {
            if (e.target.classList.contains('color-name-input')) {
                const name = e.target.value.trim().toLowerCase();
                const card = e.target.closest('.color-variant-card');
                if (card && colorMap[name]) {
                    const colorInput = card.querySelector('.color-hex-input');
                    const preview = card.querySelector('.color-preview');
                    const hexText = card.querySelector('.color-hex-text');
                    if (colorInput) {
                        colorInput.value = colorMap[name];
                        if (preview) preview.style.backgroundColor = colorMap[name];
                        if (hexText) hexText.textContent = colorMap[name];
                    }
                }
                updateColorVariantsData();
            }
        });

        function showToast(message, isError = false) {
            const toast = document.getElementById('toast');
            const toastMsg = document.getElementById('toastMessage');
            const icon = toast.querySelector('i');
            
            toastMsg.textContent = message;
            icon.className = isError ? 'fas fa-exclamation-circle text-red-400' : 'fas fa-check-circle text-emerald-400';
            toast.classList.remove('hidden');
            
            setTimeout(() => {
                toast.classList.add('hidden');
            }, 3000);
        }

        async function loadSynchronizedDatabaseCatalog() {
            try {
                const response = await fetch('/api/products');
                if (!response.ok) throw new Error('Failed to fetch products');
                
                localInMemoryCache = await response.json();
                
                document.getElementById('catalogBadgeCount').innerText = localInMemoryCache.length + ' Items';
                const tableBody = document.getElementById('realtimeCatalogTableBody');
                const emptyState = document.getElementById('tableEmptyStateMessage');
                
                tableBody.innerHTML = '';

                if (localInMemoryCache.length === 0) {
                    emptyState.classList.remove('hidden');
                    return;
                }
                emptyState.classList.add('hidden');

                localInMemoryCache.forEach((p, index) => {
                    const statusClass = p.Status === 'Publish' ? 'bg-emerald-100 text-emerald-800 border-emerald-200' : 
                                      p.Status === 'Pending' ? 'bg-amber-100 text-amber-800 border-amber-200' : 
                                      'bg-gray-100 text-gray-800 border-gray-200';
                    const formattedPrice = `$${p.Price.toFixed(2)}`;
                    
                    // Get color swatches for display
                    let colorSwatches = '';
                    if (p.ColorVariants && p.ColorVariants.length > 0) {
                        colorSwatches = p.ColorVariants.slice(0, 3).map(c => 
                            `<span class='w-3 h-3 rounded-full inline-block border border-gray-200' style='background-color: ${c.HexCode}' title='${c.Name}'></span>`
                        ).join('');
                        if (p.ColorVariants.length > 3) {
                            colorSwatches += `<span class='text-[8px] text-gray-400'>+${p.ColorVariants.length - 3}</span>`;
                        }
                    }

                    tableBody.innerHTML += `
                        <tr class='hover:bg-slate-50/70 transition group table-row-enter' style='animation-delay: ${index * 50}ms'>
                            <td class='p-3 font-mono font-bold text-slate-400'>${p.Id}</td>
                            <td class='p-3 font-mono tracking-tight text-slate-600 text-[10px]'>${p.Sku || 'N/A'}</td>
                            <td class='p-3'>
                                <div class='font-bold text-[#1d2327] group-hover:text-[#2271b1] transition text-sm'>${p.Name}</div>
                                ${colorSwatches ? `<div class='flex gap-1 mt-1'>${colorSwatches}</div>` : ''}
                                <div class='text-slate-400 text-[10px] truncate max-w-md mt-0.5 font-normal'>${p.ShortDescription || 'No description'}</div>
                            </td>
                            <td class='p-3 font-bold text-slate-900 text-sm'>${formattedPrice}</td>
                            <td class='p-3 font-medium'>
                                <span class='${p.Stock <= 5 ? 'text-amber-600 font-bold' : 'text-slate-600'}'>${p.Stock}</span>
                            </td>
                            <td class='p-3'>
                                <span class='text-[10px] font-bold px-2 py-1 rounded-md border tracking-wide uppercase ${statusClass}'>
                                    ${p.Status}
                                </span>
                            </td>
                            <td class='p-3 text-center'>
                                <div class='flex items-center justify-center gap-1.5'>
                                    <a href='/products/${p.Id}' target='_blank' class='inline-flex items-center gap-1 bg-slate-100 hover:bg-[#2271b1] text-slate-700 hover:text-white font-semibold py-1.5 px-2.5 border border-slate-300 hover:border-[#135e96] rounded-md transition cursor-pointer text-[10px] shadow-sm'>
                                        <i class='fas fa-eye'></i> View
                                    </a>
                                    <button onclick='triggerEditStateMode(${p.Id})' type='button' class='inline-flex items-center gap-1 bg-amber-50 hover:bg-amber-500 text-amber-700 hover:text-white font-semibold py-1.5 px-2.5 border border-amber-300 hover:border-amber-600 rounded-md transition cursor-pointer text-[10px] shadow-sm'>
                                        <i class='fas fa-edit'></i> Edit
                                    </button>
                                    <button onclick='triggerDeleteExecution(${p.Id})' type='button' class='inline-flex items-center gap-1 bg-rose-50 hover:bg-rose-600 text-rose-700 hover:text-white font-semibold py-1.5 px-2.5 border border-rose-300 hover:border-rose-700 rounded-md transition cursor-pointer text-[10px] shadow-sm'>
                                        <i class='fas fa-trash'></i> Delete
                                    </button>
                                </div>
                            </td>
                        </tr>`;
                });
            } catch (err) {
                console.error('Error fetching products:', err);
                showToast('Failed to load products from database', true);
            }
        }

        function handleGalleryUpload(input) {
            const files = Array.from(input.files);
            files.forEach(file => {
                const reader = new FileReader();
                reader.onload = (e) => {
                    currentGalleryBase64.push(e.target.result);
                    renderGalleryPreview();
                };
                reader.readAsDataURL(file);
            });
            input.value = ''; // Reset input
        }

        function renderGalleryPreview() {
            const container = document.getElementById('galleryPreview');
            container.innerHTML = '';
            if (currentGalleryBase64.length === 0) {
                container.innerHTML = `<span class='text-xs text-gray-400'>No images uploaded yet</span>`;
                return;
            }
            currentGalleryBase64.forEach((src, idx) => {
                container.innerHTML += `
                    <div class='relative group w-16 h-16 border border-gray-200 rounded-lg overflow-hidden shadow-sm'>
                        <img src='${src}' class='w-full h-full object-cover'>
                        <button onclick='removeGalleryImage(${idx})' class='absolute top-0 right-0 bg-rose-600 text-white text-[8px] w-5 h-5 flex items-center justify-center opacity-0 group-hover:opacity-100 cursor-pointer transition'>
                            <i class='fas fa-times'></i>
                        </button>
                    </div>`;
            });
        }

        function removeGalleryImage(index) {
            currentGalleryBase64.splice(index, 1);
            renderGalleryPreview();
        }

        function triggerEditStateMode(productId) {
            const product = localInMemoryCache.find(x => x.Id === productId);
            if (!product) {
                showToast('Product not found', true);
                return;
            }

            document.getElementById('pId').value = product.Id;
            document.getElementById('pName').value = product.Name;
            document.getElementById('pPrice').value = product.Price;
            document.getElementById('pSalePrice').value = product.SalePrice || '';
            document.getElementById('pStock').value = product.Stock;
            document.getElementById('pSku').value = product.Sku || '';
            document.getElementById('pStatus').value = product.Status || 'Draft';
            document.getElementById('pShortDesc').value = product.ShortDescription || '';
            document.getElementById('pLongDesc').value = product.LongDescription || '';
            document.getElementById('pSizes').value = (product.Sizes || []).join(', ');
            
            currentGalleryBase64 = [...(product.GalleryImages || [])];
            renderGalleryPreview();

            // Load color variants
            const container = document.getElementById('colorVariantsContainer');
            container.innerHTML = '';
            if (product.ColorVariants && product.ColorVariants.length > 0) {
                product.ColorVariants.forEach(variant => {
                    addColorVariant(variant.Name, variant.HexCode, variant.ImageUrl);
                });
            }

            document.getElementById('formPanelTitle').innerHTML = `<i class='fas fa-edit text-amber-500'></i> Edit Product (ID: #${product.Id})`;
            document.getElementById('submitFormBtn').innerHTML = `<i class='fas fa-save'></i> Update Product`;
            document.getElementById('cancelEditBtn').classList.remove('hidden');
            window.scrollTo({ top: 0, behavior: 'smooth' });
            showToast(`Editing product: ${product.Name}`);
        }

        function resetApplicationFormState() {
            document.getElementById('pId').value = '0';
            document.getElementById('pName').value = '';
            document.getElementById('pPrice').value = '';
            document.getElementById('pSalePrice').value = '';
            document.getElementById('pStock').value = '';
            document.getElementById('pSku').value = '';
            document.getElementById('pShortDesc').value = '';
            document.getElementById('pLongDesc').value = '';
            document.getElementById('pSizes').value = '';
            document.getElementById('pStatus').value = 'Publish';
            
            currentGalleryBase64 = [];
            renderGalleryPreview();
            
            document.getElementById('colorVariantsContainer').innerHTML = '';
            colorVariants = [];

            document.getElementById('formPanelTitle').innerHTML = `<i class='fas fa-plus-circle text-emerald-500'></i> Add New Product`;
            document.getElementById('submitFormBtn').innerHTML = `<i class='fas fa-save'></i> Save & Publish Product`;
            document.getElementById('cancelEditBtn').classList.add('hidden');
        }

        async function triggerDeleteExecution(productId) {
            if (!confirm('Are you sure you want to delete this product?')) return;
            
            try {
                const res = await fetch(`/api/products/${productId}`, { method: 'DELETE' });
                if (res.ok) {
                    await loadSynchronizedDatabaseCatalog();
                    if (document.getElementById('pId').value == productId) {
                        resetApplicationFormState();
                    }
                    showToast('Product deleted successfully');
                } else {
                    showToast('Failed to delete product', true);
                }
            } catch (err) {
                showToast('Error deleting product', true);
            }
        }

        async function submitProductForm(e) {
            e.preventDefault();
            
            const targetName = document.getElementById('pName').value.trim();
            if(!targetName) {
                showToast('Please enter a product name', true);
                return;
            }

            updateColorVariantsData();

            const payloadData = {
                id: parseInt(document.getElementById('pId').value) || 0,
                name: targetName,
                price: parseFloat(document.getElementById('pPrice').value) || 0,
                salePrice: parseFloat(document.getElementById('pSalePrice').value) || 0,
                stock: parseInt(document.getElementById('pStock').value) || 0,
                sku: document.getElementById('pSku').value.trim(),
                status: document.getElementById('pStatus').value,
                shortDescription: document.getElementById('pShortDesc').value.trim(),
                longDescription: document.getElementById('pLongDesc').value.trim(),
                colors: colorVariants.map(v => v.name),
                sizes: document.getElementById('pSizes').value.split(',').map(s => s.trim()).filter(s => s),
                galleryImages: currentGalleryBase64,
                colorVariants: colorVariants
            };

            try {
                const response = await fetch('/api/products', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payloadData)
                });

                if (response.ok) {
                    const isEdit = parseInt(document.getElementById('pId').value) > 0;
                    resetApplicationFormState();
                    await loadSynchronizedDatabaseCatalog();
                    showToast(isEdit ? 'Product updated successfully!' : 'Product created successfully!');
                } else {
                    showToast('Failed to save product', true);
                }
            } catch (error) {
                showToast('Error saving product', true);
            }
        }

        // Initialize with one default color variant
        addColorVariant('Black', '#111111', '');
        
        // Load products on page load
        loadSynchronizedDatabaseCatalog();
        
        // Auto-refresh every 30 seconds
        setInterval(loadSynchronizedDatabaseCatalog, 30000);
    </script>
</body>
</html>";
        }
    }
}