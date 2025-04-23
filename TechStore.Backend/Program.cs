using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;
using System.Text.Json;


var builder = WebApplication.CreateBuilder(args);

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5014") // Updated to match the frontend
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
var app = builder.Build();

app.UseCors("AllowFrontend");
app.UseAuthorization();
app.MapControllers();

// Set up users database
string usersDbPath = "databases/users.db";
Directory.CreateDirectory("databases");

using (var connection = new SqliteConnection($"Data Source={usersDbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE IF NOT EXISTS users (uname TEXT PRIMARY KEY, password TEXT NOT NULL);";
    command.ExecuteNonQuery();
}

// Set up products database
string productsDbPath = "databases/products.db";

using (var connection = new SqliteConnection($"Data Source={productsDbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL, 
            description TEXT,
            category TEXT NOT NULL,
            image BLOB,
            rating INTEGER NOT NULL,
            price REAL,
            isTop INTEGER
        );
    ";
    command.ExecuteNonQuery();
}

// Set up FAQ database
string faqDbPath = "databases/faq.db";
using (var faqConn = new SqliteConnection($"Data Source={faqDbPath}"))
{
    faqConn.Open();
    var faqCmd = faqConn.CreateCommand();
    faqCmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS faq (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            question TEXT NOT NULL,
            answer   TEXT NOT NULL
        );
    ";
    faqCmd.ExecuteNonQuery();

    // Seed 10 generic FAQs if empty
    var countCmd = faqConn.CreateCommand();
    countCmd.CommandText = "SELECT COUNT(*) FROM faq";
    if ((long)countCmd.ExecuteScalar() == 0)
    {
        var questions = new (string Q, string A)[]
        {
            ("What is Tech Store?", "An online marketplace for tech gadgets."),
            ("How do I create an account?", "Click Sign Up and enter your details."),
            ("Is my password secure?", "We store passwords securely, never in plain text."),
            ("Can I return a product?", "Yes—see our Returns Policy on the site."),
            ("How do I reset my password?", "Use the 'Forgot Password' link on login."),
            ("What payment methods are accepted?", "Visa, MasterCard, PayPal, and more."),
            ("How long does shipping take?", "Typically 3–5 business days."),
            ("Do you ship internationally?", "Yes, we ship to over 50 countries."),
            ("How do I contact support?", "Email support@techstore.com."),
            ("Are there bulk discounts?", "Contact sales@techstore.com for details.")
        };

        using var tx = faqConn.BeginTransaction();
        var insert = faqConn.CreateCommand();
        insert.CommandText = "INSERT INTO faq (question,answer) VALUES (@q,@a)";
        insert.Parameters.Add(new SqliteParameter("@q", ""));
        insert.Parameters.Add(new SqliteParameter("@a", ""));
        foreach (var (q, a) in questions)
        {
            insert.Parameters["@q"].Value = q;
            insert.Parameters["@a"].Value = a;
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }
}



// Set up cart database table 

string cartDbPath = "databases/cart.db";
using (var connection = new SqliteConnection($"Data Source={cartDbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS cart (
            cart_id INTEGER PRIMARY KEY AUTOINCREMENT,
            uname TEXT NOT NULL,
            product_id INTEGER NOT NULL,
            quantity INTEGER NOT NULL
        );
    ";
    command.ExecuteNonQuery();
}

// setup wishlist db
string wishlistDbPath = "databases/wishlist.db";
using (var connection = new SqliteConnection($"Data Source={wishlistDbPath}"))
{
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        CREATE TABLE IF NOT EXISTS wishlist (
            wishlist_id INTEGER PRIMARY KEY AUTOINCREMENT,
            uname TEXT NOT NULL,
            product_id INTEGER NOT NULL
        );
    ";
    command.ExecuteNonQuery();
}



// User Profiles DB
string profilesDbPath = "databases/user_profiles.db";
using (var conn = new SqliteConnection($"Data Source={profilesDbPath}"))
{
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS user_profiles (
            uname TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            address TEXT NOT NULL,
            image BLOB,
            wallet_balance REAL NOT NULL
        );";
    cmd.ExecuteNonQuery();
}


app.MapPost("/api/auth/signup", async (HttpContext context) =>
{
    var authRequest = await context.Request.ReadFromJsonAsync<AuthRequest>();
    if (authRequest is null)
    {
        return Results.BadRequest("Invalid payload.");
    }
    string uname = authRequest.Username.ToLower();
    string password = authRequest.Password;

    if (!Regex.IsMatch(password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*[_#@$]).{8,}$"))
        return Results.BadRequest("Invalid password format");

    using (var connection = new SqliteConnection($"Data Source={usersDbPath}"))
    {
        connection.Open();
        var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname";
        checkCmd.Parameters.AddWithValue("@uname", uname);
        if ((long)checkCmd.ExecuteScalar() > 0)
            return Results.BadRequest("Username already exists");

        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = "INSERT INTO users (uname, password) VALUES (@uname, @password)";
        insertCmd.Parameters.AddWithValue("@uname", uname);
        insertCmd.Parameters.AddWithValue("@password", password);
        insertCmd.ExecuteNonQuery();
    }
    return Results.Ok("Signup successful");
});

// Login Endpoint
app.MapPost("/api/auth/login", async (HttpContext context) =>
{
    var authRequest = await context.Request.ReadFromJsonAsync<AuthRequest>();
    if (authRequest is null)
    {
        return Results.BadRequest("Invalid payload.");
    }
    string uname = authRequest.Username.ToLower();
    string password = authRequest.Password;

    using (var connection = new SqliteConnection($"Data Source={usersDbPath}"))
    {
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname AND password = @password";
        command.Parameters.AddWithValue("@uname", uname);
        command.Parameters.AddWithValue("@password", password);
        if ((long)command.ExecuteScalar() > 0)
            return Results.Ok("Login successful");
    }
    return Results.BadRequest("Invalid credentials");
});

app.MapPost("/admin", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();

    string name = form["name"].ToString();
    string description = form["description"].ToString();
    string category = form["category"].ToString();
    int.TryParse(form["rating"].ToString(), out int rating);
    double.TryParse(form["price"].ToString(), out double price);
    int.TryParse(form["isTop"].ToString(), out int isTop); // 0 = false, 1 = true

    // Handling image file
    byte[]? imageBytes = null;
    if (form.Files.Count > 0)
    {
        var imageFile = form.Files["image"];
        if (imageFile != null)
        {
            using var ms = new MemoryStream();
            await imageFile.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }
    }

    using (var connection = new SqliteConnection($"Data Source={productsDbPath}"))
    {
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO products (name, description, category, image, rating, price, isTop) 
            VALUES (@name, @description, @category, @image, @rating, @price, @isTop)";
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@description", description);
        command.Parameters.AddWithValue("@category", category);
        command.Parameters.AddWithValue("@image", imageBytes == null ? DBNull.Value : (object)imageBytes);
        command.Parameters.AddWithValue("@rating", rating);
        command.Parameters.AddWithValue("@price", price);
        command.Parameters.AddWithValue("@isTop", isTop);

        command.ExecuteNonQuery();
    }

    return Results.Ok("Product added successfully.");
});

app.MapGet("/api/products",async () =>
{
    var products = new List<object>();
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();

    var cmd = connection.CreateCommand();
    cmd.CommandText = @"
        SELECT id, name, description, category, image, rating, price, isTop
        FROM products;";
    using var reader = cmd.ExecuteReader();
    while (reader.Read())
    {
        products.Add(new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            category = reader.GetString(3),
            image = reader.IsDBNull(4) ? null : Convert.ToBase64String((byte[])reader[4]),
            rating = reader.GetInt32(5),
            price = reader.GetDouble(6),
            isTop = reader.GetInt32(7) == 1
        });
    }

    return Results.Json(products);
});

app.MapGet("/product/{id:int}", async (int id) =>
{
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT id, name, description, image, rating, price, isTop 
        FROM products 
        WHERE id = @id";
    command.Parameters.AddWithValue("@id", id);
    
    using var reader = command.ExecuteReader();
    if (reader.Read())
    {
        var product = new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            image = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
            rating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            price = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
            isTop = reader.IsDBNull(6) ? false : reader.GetInt32(6) == 1
        };
        return Results.Json(product);
    }
    return Results.NotFound("Product not found.");
});

app.MapGet("/topselling",async () =>
{
    string productsDbPath = "databases/products.db";
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT id, name, description, image, rating, price, isTop 
        FROM products 
        WHERE isTop = 1
        ORDER BY name ASC";
    
    using var reader = command.ExecuteReader();
    var products = new List<object>();
    while (reader.Read())
    {
        var product = new
        {
            id = reader.GetInt32(0),
            name = reader.GetString(1),
            description = reader.IsDBNull(2) ? null : reader.GetString(2),
            image = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
            rating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            price = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
            isTop = reader.IsDBNull(6) ? false : reader.GetInt32(6) == 1
        };
        products.Add(product);
    }
    return Results.Json(products);
});


app.MapGet("/homepage",async () =>
{
    string productsDbPath = "databases/products.db";
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();

    // Get top selling products (isTop = 1)
    var productCommand = connection.CreateCommand();
    productCommand.CommandText = @"
        SELECT id, name, description, image, rating, price, isTop 
        FROM products 
        WHERE isTop = 1
        ORDER BY name ASC";
    var products = new List<object>();
    using (var reader = productCommand.ExecuteReader())
    {
        while (reader.Read())
        {
            var product = new
            {
                id = reader.GetInt32(0),
                name = reader.GetString(1),
                description = reader.IsDBNull(2) ? null : reader.GetString(2),
                image = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
                rating = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                price = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5),
                isTop = reader.IsDBNull(6) ? false : reader.GetInt32(6) == 1
            };
            products.Add(product);
        }
    }

    // Get distinct categories with the image of the first product in each category
    var categoryDisplayList = new List<object>();
    var distinctCmd = connection.CreateCommand();
    distinctCmd.CommandText = "SELECT DISTINCT category FROM products ORDER BY category ASC";
    using (var reader = distinctCmd.ExecuteReader())
    {
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                var categoryName = reader.GetString(0);
                var imageCmd = connection.CreateCommand();
                imageCmd.CommandText = "SELECT image FROM products WHERE category = @cat AND image IS NOT NULL ORDER BY name ASC LIMIT 1";
                imageCmd.Parameters.AddWithValue("@cat", categoryName);
                object imageObj = imageCmd.ExecuteScalar();
                string? imageBase64 = (imageObj != null && imageObj != DBNull.Value)
                    ? Convert.ToBase64String((byte[])imageObj)
                    : null;
                categoryDisplayList.Add(new { category = categoryName, image = imageBase64 });
            }
        }
    }

    return Results.Json(new { products, categories = categoryDisplayList });
});

app.MapGet("/categories",async () =>
{
    string productsDbPath = "databases/products.db";
    using var connection = new SqliteConnection($"Data Source={productsDbPath}");
    connection.Open();

    var distinctCmd = connection.CreateCommand();
    distinctCmd.CommandText = "SELECT DISTINCT category FROM products ORDER BY category ASC";
    var categories = new List<object>();
    
    using (var reader = distinctCmd.ExecuteReader())
    {
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                string cat = reader.GetString(0);
                // Get the image of the first product in this category (if exists)
                var imageCmd = connection.CreateCommand();
                imageCmd.CommandText = "SELECT image FROM products WHERE category = @cat AND image IS NOT NULL ORDER BY name ASC LIMIT 1";
                imageCmd.Parameters.AddWithValue("@cat", cat);
                object imageObj = imageCmd.ExecuteScalar();
                string? imageBase64 = (imageObj != null && imageObj != DBNull.Value)
                    ? Convert.ToBase64String((byte[])imageObj)
                    : null;
                categories.Add(new { category = cat, image = imageBase64 });
            }
        }
    }
    return Results.Json(categories);
});


// Add to cart endpoint
app.MapPost("/cart", async (HttpContext context) =>
{
    // Parse incoming JSON request
    var jsonDoc = await JsonDocument.ParseAsync(context.Request.Body);
    var root = jsonDoc.RootElement;

    // Validate required fields
    if (!root.TryGetProperty("uname", out var unameProp) ||
        !root.TryGetProperty("productId", out var productIdProp) ||
        !root.TryGetProperty("quantity", out var quantityProp))
    {
        return Results.BadRequest("Missing one or more required fields.");
    }

    string uname = unameProp.GetString()?.ToLower() ?? "";
    int productId = productIdProp.GetInt32();
    int quantity = quantityProp.GetInt32();

    // Check if the user exists
    using (var userConnection = new SqliteConnection($"Data Source={usersDbPath}"))
    {
        await userConnection.OpenAsync();
        using var userCheckCmd = userConnection.CreateCommand();
        userCheckCmd.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname";
        userCheckCmd.Parameters.AddWithValue("@uname", uname);
        var result = await userCheckCmd.ExecuteScalarAsync();
        if ((long)result == 0)
        {
            return Results.BadRequest("User does not exist.");
        }
    }

    // Add to cart logic
    using (var connection = new SqliteConnection($"Data Source={cartDbPath}"))
    {
        await connection.OpenAsync();

        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT quantity FROM cart
            WHERE uname = @uname AND product_id = @product_id";
        checkCmd.Parameters.AddWithValue("@uname", uname);
        checkCmd.Parameters.AddWithValue("@product_id", productId);

        var existingQuantityObj = await checkCmd.ExecuteScalarAsync();

        if (existingQuantityObj != null)
        {
            int existingQuantity = Convert.ToInt32(existingQuantityObj);
            int newQuantity = existingQuantity + quantity;

            if (newQuantity <= 0)
            {
                using var deleteCmd = connection.CreateCommand();
                deleteCmd.CommandText = @"
                    DELETE FROM cart
                    WHERE uname = @uname AND product_id = @product_id";
                deleteCmd.Parameters.AddWithValue("@uname", uname);
                deleteCmd.Parameters.AddWithValue("@product_id", productId);

                try
                {
                    await deleteCmd.ExecuteNonQueryAsync();
                    return Results.Ok("Cart item removed as quantity reached zero or below.");
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to remove cart item: {ex.Message}");
                }
            }
            else
            {
                using var updateCmd = connection.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE cart
                    SET quantity = @newQuantity
                    WHERE uname = @uname AND product_id = @product_id";
                updateCmd.Parameters.AddWithValue("@newQuantity", newQuantity);
                updateCmd.Parameters.AddWithValue("@uname", uname);
                updateCmd.Parameters.AddWithValue("@product_id", productId);

                try
                {
                    await updateCmd.ExecuteNonQueryAsync();
                    return Results.Ok("Cart item quantity updated.");
                }
                catch (Exception ex)
                {
                    return Results.Problem($"Failed to update cart item: {ex.Message}");
                }
            }
        }
        else
        {
            using var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO cart (uname, product_id, quantity)
                VALUES (@uname, @product_id, @quantity)";
            insertCmd.Parameters.AddWithValue("@uname", uname);
            insertCmd.Parameters.AddWithValue("@product_id", productId);
            insertCmd.Parameters.AddWithValue("@quantity", quantity);

            try
            {
                await insertCmd.ExecuteNonQueryAsync();
                return Results.Ok("Item added to cart.");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Failed to add item to cart: {ex.Message}");
            }
        }
    }
});



// Get all cart items for a user by their username
app.MapGet("/cart/{uname}", async (string uname) =>
{
    var items = new List<object>();

    var alias = $"prod_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

    await using var connection = new SqliteConnection($"Data Source={cartDbPath}");
    await connection.OpenAsync();

    // Attach products DB
    await using (var attachCmd = connection.CreateCommand())
    {
        attachCmd.CommandText = $"ATTACH DATABASE '{productsDbPath}' AS {alias};";
        await attachCmd.ExecuteNonQueryAsync();
    }

    // Query the cart with the attached alias
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = $@"
            SELECT 
                c.cart_id,
                c.product_id,
                p.name AS product_name,
                p.image AS product_image,
                p.price AS product_price,
                c.quantity
            FROM cart c
            JOIN {alias}.products p ON c.product_id = p.id
            WHERE LOWER(c.uname) = LOWER(@uname);";
        command.Parameters.AddWithValue("@uname", uname);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                CartId = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductName = reader.GetString(2),
                ProductImage = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader["product_image"]),
                ProductPrice = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4),
                Quantity = reader.GetInt32(5)
            });
        }
    }

    // Detach products DB to avoid hitting the 10-database limit
    await using (var detachCmd = connection.CreateCommand())
    {
        detachCmd.CommandText = $"DETACH DATABASE {alias};";
        await detachCmd.ExecuteNonQueryAsync();
    }

    return Results.Json(items);
});


// clear the cart (by option or at checkout)
app.MapDelete("/cart/clear/{uname}",async (string uname) =>
{
    using var connection = new SqliteConnection($"Data Source={cartDbPath}");
    connection.Open();
    var command = connection.CreateCommand();
    command.CommandText = "DELETE FROM cart WHERE uname = @uname";
    command.Parameters.AddWithValue("@uname", uname.ToLower());
    int deleted = command.ExecuteNonQuery();
    return Results.Ok($"{deleted} item(s) removed from cart.");
});

app.MapGet("/profile/{username}", (string username) =>
{
    username = username.ToLower();
    // 1) Load profile
    using var profileConn = new SqliteConnection($"Data Source={profilesDbPath}");
    profileConn.Open();
    var profileCmd = profileConn.CreateCommand();
    profileCmd.CommandText = @"
        SELECT name, address, image, wallet_balance
        FROM user_profiles
        WHERE uname = @uname";
    profileCmd.Parameters.AddWithValue("@uname", username);

    using var reader = profileCmd.ExecuteReader();
    if (!reader.Read())
        return Results.NotFound("Profile not found.");

    var name = reader.GetString(0);
    var address = reader.GetString(1);
    byte[]? imageBytes = reader.IsDBNull(2) ? null : (byte[])reader[2];
    double wallet = reader.GetDouble(3);
    string? imageBase64 = imageBytes is null ? null : Convert.ToBase64String(imageBytes);

    // 2) Compute stats from cart
    using var cartConn = new SqliteConnection($"Data Source={cartDbPath}");
    cartConn.Open();
    var statsCmd = cartConn.CreateCommand();
    statsCmd.CommandText = @"
        SELECT 
          COALESCE(SUM(quantity),0) AS itemsBought,
          COUNT(*) AS totalTransactions
        FROM cart
        WHERE LOWER(uname)=@uname";
    statsCmd.Parameters.AddWithValue("@uname", username);

    using var statsReader = statsCmd.ExecuteReader();
    statsReader.Read();
    int itemsBought = statsReader.GetInt32(0);
    int totalTransactions = statsReader.GetInt32(1);

    var result = new
    {
        uname = username,
        name,
        address,
        image = imageBase64,
        walletBalance = wallet,
        itemsBought,
        totalTransactions
    };
    return Results.Json(result);
});


// Get all wishlist items for a user by their username
app.MapGet("/wishlist/{uname}", async (string uname) =>
{
    var items = new List<object>();

    var alias = $"prod_{Guid.NewGuid().ToString("N").Substring(0, 6)}";

    await using var connection = new SqliteConnection($"Data Source={wishlistDbPath}");
    await connection.OpenAsync();

    // Attach products DB
    await using (var attachCmd = connection.CreateCommand())
    {
        attachCmd.CommandText = $"ATTACH DATABASE '{productsDbPath}' AS {alias};";
        await attachCmd.ExecuteNonQueryAsync();
    }

    // Query the wishlist with the attached alias
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = $@"
            SELECT 
                w.product_id,
                p.name AS product_name,
                p.image AS product_image,
                p.price AS product_price
            FROM wishlist w
            JOIN {alias}.products p ON w.product_id = p.id
            WHERE LOWER(w.uname) = LOWER(@uname);";
        command.Parameters.AddWithValue("@uname", uname);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new
            {
                ProductId = reader.GetInt32(0),
                ProductName = reader.GetString(1),
                ProductImage = reader.IsDBNull(2) ? null : Convert.ToBase64String((byte[])reader["product_image"]),
                ProductPrice = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3)
            });
        }
    }

    // Detach products DB to avoid exceeding attached database limit
    await using (var detachCmd = connection.CreateCommand())
    {
        detachCmd.CommandText = $"DETACH DATABASE {alias};";
        await detachCmd.ExecuteNonQueryAsync();
    }

    return Results.Json(items);
});


//add to wishlist
app.MapPost("/wishlist", async (HttpContext context) =>
{
    var jsonDoc = await JsonDocument.ParseAsync(context.Request.Body);
    var root = jsonDoc.RootElement;

    if (!root.TryGetProperty("uname", out var unameProp) ||
        !root.TryGetProperty("productId", out var productIdProp))
    {
        return Results.BadRequest("Missing one or more required fields.");
    }

    string uname = unameProp.GetString()?.ToLower() ?? "";
    int productId = productIdProp.GetInt32();

    using (var userConnection = new SqliteConnection($"Data Source={usersDbPath}"))
    {
        await userConnection.OpenAsync();
        using var userCheckCmd = userConnection.CreateCommand();
        userCheckCmd.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname";
        userCheckCmd.Parameters.AddWithValue("@uname", uname);
        var result = await userCheckCmd.ExecuteScalarAsync();
        if ((long)result == 0)
        {
            return Results.BadRequest("User does not exist.");
        }
    }

    using (var connection = new SqliteConnection($"Data Source={wishlistDbPath}"))
    {
        await connection.OpenAsync();

        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = @"
            SELECT COUNT(*) FROM wishlist
            WHERE uname = @uname AND product_id = @product_id";
        checkCmd.Parameters.AddWithValue("@uname", uname);
        checkCmd.Parameters.AddWithValue("@product_id", productId);

        var exists = (long)(await checkCmd.ExecuteScalarAsync()) > 0;
        if (exists)
        {
            return Results.Ok("Item is already in wishlist.");
        }

        using var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO wishlist (uname, product_id)
            VALUES (@uname, @product_id)";
        insertCmd.Parameters.AddWithValue("@uname", uname);
        insertCmd.Parameters.AddWithValue("@product_id", productId);

        try
        {
            await insertCmd.ExecuteNonQueryAsync();
            return Results.Ok("Item added to wishlist.");
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to add item to wishlist: {ex.Message}");
        }
    }
});

//remove items from wishlist and add them to cart
app.MapPost("/wishlist/checkout/{uname}", async (string uname) =>
{
    var wishlistItems = new List<int>(); // product IDs

    using (var connection = new SqliteConnection($"Data Source={wishlistDbPath}"))
    {
        await connection.OpenAsync();

        using var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT product_id FROM wishlist WHERE uname = @uname";
        selectCmd.Parameters.AddWithValue("@uname", uname.ToLower());

        using var reader = await selectCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            wishlistItems.Add(reader.GetInt32(0));
        }
    }

    if (wishlistItems.Count == 0)
    {
        return Results.Ok("No wishlist items to add to cart.");
    }

    using (var cartConnection = new SqliteConnection($"Data Source={cartDbPath}"))
    {
        await cartConnection.OpenAsync();
        foreach (var productId in wishlistItems)
        {
            using var checkCmd = cartConnection.CreateCommand();
            checkCmd.CommandText = "SELECT quantity FROM cart WHERE uname = @uname AND product_id = @product_id";
            checkCmd.Parameters.AddWithValue("@uname", uname);
            checkCmd.Parameters.AddWithValue("@product_id", productId);

            var existingQuantityObj = await checkCmd.ExecuteScalarAsync();

            if (existingQuantityObj != null)
            {
                int newQuantity = Convert.ToInt32(existingQuantityObj) + 1;

                using var updateCmd = cartConnection.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE cart SET quantity = @newQuantity
                    WHERE uname = @uname AND product_id = @product_id";
                updateCmd.Parameters.AddWithValue("@newQuantity", newQuantity);
                updateCmd.Parameters.AddWithValue("@uname", uname);
                updateCmd.Parameters.AddWithValue("@product_id", productId);
                await updateCmd.ExecuteNonQueryAsync();
            }
            else
            {
                using var insertCmd = cartConnection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO cart (uname, product_id, quantity)
                    VALUES (@uname, @product_id, 1)";
                insertCmd.Parameters.AddWithValue("@uname", uname);
                insertCmd.Parameters.AddWithValue("@product_id", productId);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }
    }

    using (var connection = new SqliteConnection($"Data Source={wishlistDbPath}"))
    {
        await connection.OpenAsync();
        using var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM wishlist WHERE uname = @uname";
        deleteCmd.Parameters.AddWithValue("@uname", uname.ToLower());
        await deleteCmd.ExecuteNonQueryAsync();
    }

    return Results.Ok($"{wishlistItems.Count} wishlist item(s) moved to cart and wishlist cleared.");
});

// Remove a specific item from the wishlist
app.MapDelete("/wishlist/remove/{uname}/{productId}", async (string uname, int productId) =>
{
    using var connection = new SqliteConnection($"Data Source={wishlistDbPath}");
    await connection.OpenAsync();

    using var deleteCmd = connection.CreateCommand();
    deleteCmd.CommandText = @"
        DELETE FROM wishlist
        WHERE uname = @uname AND product_id = @product_id";
    deleteCmd.Parameters.AddWithValue("@uname", uname.ToLower());
    deleteCmd.Parameters.AddWithValue("@product_id", productId);

    try
    {
        int affected = await deleteCmd.ExecuteNonQueryAsync();
        return affected > 0
            ? Results.Ok("Item removed from wishlist.")
            : Results.NotFound("Item not found in wishlist.");
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to remove item from wishlist: {ex.Message}");
    }
});

// POST to create profile if not exists
app.MapPost("/profile/{username}", async (string username, HttpContext ctx) =>
{
    username = username.ToLower();
    var req = await ctx.Request.ReadFromJsonAsync<ProfileRequest>();
    if (req is null)
        return Results.BadRequest("Invalid payload.");

    byte[]? imageBytes = null;
    if (!string.IsNullOrEmpty(req.ImageBase64))
        imageBytes = Convert.FromBase64String(req.ImageBase64);

    using var conn = new SqliteConnection($"Data Source={profilesDbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        INSERT OR REPLACE INTO user_profiles 
          (uname, name, address, image, wallet_balance)
        VALUES (@uname,@name,@address,@image,@wallet)";
    cmd.Parameters.AddWithValue("@uname", username);
    cmd.Parameters.AddWithValue("@name", req.Name);
    cmd.Parameters.AddWithValue("@address", req.Address);
    cmd.Parameters.AddWithValue("@image", imageBytes is null ? DBNull.Value : (object)imageBytes);
    cmd.Parameters.AddWithValue("@wallet", req.WalletBalance);

    cmd.ExecuteNonQuery();
    return Results.Ok("Profile saved.");
});

// 📖 GET 4 random FAQs (for the public site)
app.MapGet("/faqs/random", () =>
{
    using var conn = new SqliteConnection($"Data Source={faqDbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id,question,answer FROM faq ORDER BY RANDOM() LIMIT 4";
    var list = new List<object>();
    using var rdr = cmd.ExecuteReader();
    while (rdr.Read())
    {
        list.Add(new {
            id       = rdr.GetInt32(0),
            question = rdr.GetString(1),
            answer   = rdr.GetString(2)
        });
    }
    return Results.Json(list);
});

// 📋 GET all FAQs (admin view)
app.MapGet("/faqs", () =>
{
    using var conn = new SqliteConnection($"Data Source={faqDbPath}");
    conn.Open();
    var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT id,question,answer FROM faq ORDER BY id";
    var list = new List<object>();
    using var rdr = cmd.ExecuteReader();
    while (rdr.Read())
    {
        list.Add(new {
            id       = rdr.GetInt32(0),
            question = rdr.GetString(1),
            answer   = rdr.GetString(2)
        });
    }
    return Results.Json(list);
});

// ➕ POST new FAQ (superuser only)
app.MapPost("/faqs", async (HttpContext http, FAQEntry faq) =>
{

    // insert into your faq.db
    using var conn = new SqliteConnection($"Data Source={faqDbPath}");
    conn.Open();

    var cmd = conn.CreateCommand();
    cmd.CommandText = "INSERT INTO faq (question, answer) VALUES (@q, @a)";
    cmd.Parameters.AddWithValue("@q", faq.Question);
    cmd.Parameters.AddWithValue("@a", faq.Answer);
    cmd.ExecuteNonQuery();

    return Results.Ok("FAQ added successfully.");
});


app.Run();

public record FAQEntry(string Question, string Answer);
public record AuthRequest(string Username, string Password);
// Profile create/update request payload
public record ProfileRequest(string Name, string Address, double WalletBalance, string? ImageBase64);