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

    // Check if the user exists in the users database
    using var userConnection = new SqliteConnection($"Data Source={usersDbPath}");
    userConnection.Open();
    var userCheckCmd = userConnection.CreateCommand();
    userCheckCmd.CommandText = "SELECT COUNT(*) FROM users WHERE uname = @uname";
    userCheckCmd.Parameters.AddWithValue("@uname", uname);
    long userExists = (long)userCheckCmd.ExecuteScalar();
    userConnection.Close();

    if (userExists == 0)
    {
        return Results.BadRequest("User does not exist.");
    }

    // Use the cart database connection (ensuring FK constraints remain intact)
    using var connection = new SqliteConnection($"Data Source={cartDbPath}");
    connection.Open();

    // Check if an entry already exists for this user and product
    var checkCmd = connection.CreateCommand();
    checkCmd.CommandText = @"
        SELECT quantity FROM cart
        WHERE uname = @uname AND product_id = @product_id";
    checkCmd.Parameters.AddWithValue("@uname", uname);
    checkCmd.Parameters.AddWithValue("@product_id", productId);

    var existingQuantityObj = checkCmd.ExecuteScalar();

    if (existingQuantityObj != null)
    {
        int existingQuantity = Convert.ToInt32(existingQuantityObj);
        int newQuantity = existingQuantity + quantity;

        if (newQuantity <= 0)
        {
            // Remove the item if the new quantity is zero or negative
            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = @"
                DELETE FROM cart
                WHERE uname = @uname AND product_id = @product_id";
            deleteCmd.Parameters.AddWithValue("@uname", uname);
            deleteCmd.Parameters.AddWithValue("@product_id", productId);
            try
            {
                deleteCmd.ExecuteNonQuery();
                connection.Close();
                return Results.Ok("Cart item removed as quantity reached zero or below.");
            }
            catch (Exception ex)
            {
                connection.Close();
                return Results.Problem($"Failed to remove cart item: {ex.Message}");
            }
        }
        else
        {
            // Update the cart with the new quantity
            var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE cart
                SET quantity = @newQuantity
                WHERE uname = @uname AND product_id = @product_id";
            updateCmd.Parameters.AddWithValue("@newQuantity", newQuantity);
            updateCmd.Parameters.AddWithValue("@uname", uname);
            updateCmd.Parameters.AddWithValue("@product_id", productId);
            try
            {
                updateCmd.ExecuteNonQuery();
                connection.Close();
                return Results.Ok("Cart item quantity updated.");
            }
            catch (Exception ex)
            {
                connection.Close();
                return Results.Problem($"Failed to update cart item: {ex.Message}");
            }
        }
    }
    else
    {
        // Insert a new cart item
        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO cart (uname, product_id, quantity)
            VALUES (@uname, @product_id, @quantity)";
        insertCmd.Parameters.AddWithValue("@uname", uname);
        insertCmd.Parameters.AddWithValue("@product_id", productId);
        insertCmd.Parameters.AddWithValue("@quantity", quantity);

        try
        {
            insertCmd.ExecuteNonQuery();
            connection.Close();
            return Results.Ok("Item added to cart.");
        }
        catch (Exception ex)
        {
            connection.Close();
            return Results.Problem($"Failed to add item to cart: {ex.Message}");
        }
    }
});


// Get all cart items for a user by their username
app.MapGet("/cart/{uname}",async (string uname) =>
{
    // Open connection to the cart database
    using var connection = new SqliteConnection($"Data Source={cartDbPath}");
    connection.Open();

    // Attach the products database to join product details with cart items
    var attachCmd = connection.CreateCommand();
    attachCmd.CommandText = $"ATTACH DATABASE '{productsDbPath}' AS prod;";
    attachCmd.ExecuteNonQuery();

    // Retrieve cart items with product details
    var command = connection.CreateCommand();
    command.CommandText = @"
        SELECT 
            c.cart_id,
            c.product_id,
            p.name AS product_name,
            p.image AS product_image,
            p.price AS product_price,
            c.quantity
        FROM cart c
        JOIN prod.products p ON c.product_id = p.id
        WHERE LOWER(c.uname) = LOWER(@uname);";
    command.Parameters.AddWithValue("@uname", uname);

    var items = new List<object>();
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
        items.Add(new
        {
            // Map columns to properties expected by Cart.razor
            CartId = reader.GetInt32(0),
            ProductId = reader.GetInt32(1),
            ProductName = reader.GetString(2),
            ProductImage = reader.IsDBNull(3) ? null : Convert.ToBase64String((byte[])reader[3]),
            ProductPrice = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4),
            Quantity = reader.GetInt32(5)
        });
    }

    connection.Close();
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

app.Run();

public record AuthRequest(string Username, string Password);
// Profile create/update request payload
public record ProfileRequest(string Name, string Address, double WalletBalance, string? ImageBase64);