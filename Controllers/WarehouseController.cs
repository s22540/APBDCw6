using Microsoft.AspNetCore.Mvc;
using WarehouseAPI.Models;
using Microsoft.Data.SqlClient;


namespace WarehouseAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WarehouseController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public WarehouseController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost("addProduct")]
        public async Task<IActionResult> AddProductToWarehouse([FromBody] WarehouseRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than 0.");
            }

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                await connection.OpenAsync();

               
                var checkProductQuery = "SELECT COUNT(1) FROM Product WHERE IdProduct = @ProductId";
                using (var checkProductCommand = new SqlCommand(checkProductQuery, connection))
                {
                    checkProductCommand.Parameters.AddWithValue("@ProductId", request.ProductId);
                    var productExists = (int)await checkProductCommand.ExecuteScalarAsync() > 0;

                    if (!productExists)
                    {
                        return NotFound("Product not found.");
                    }
                }

               
                var checkWarehouseQuery = "SELECT COUNT(1) FROM Warehouse WHERE IdWarehouse = @WarehouseId";
                using (var checkWarehouseCommand = new SqlCommand(checkWarehouseQuery, connection))
                {
                    checkWarehouseCommand.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                    var warehouseExists = (int)await checkWarehouseCommand.ExecuteScalarAsync() > 0;

                    if (!warehouseExists)
                    {
                        return NotFound("Warehouse not found.");
                    }
                }

               
                var checkOrderQuery = @"SELECT IdOrder, Price 
                                        FROM [Order] 
                                        WHERE IdProduct = @ProductId AND Amount = @Amount AND CreatedAt < @CreatedAt";
                int orderId = 0;
                decimal productPrice = 0;
                using (var checkOrderCommand = new SqlCommand(checkOrderQuery, connection))
                {
                    checkOrderCommand.Parameters.AddWithValue("@ProductId", request.ProductId);
                    checkOrderCommand.Parameters.AddWithValue("@Amount", request.Amount);
                    checkOrderCommand.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);
                    using (var reader = await checkOrderCommand.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            orderId = reader.GetInt32(0);
                            productPrice = reader.GetDecimal(1);
                        }
                        else
                        {
                            return NotFound("Order not found or conditions not met.");
                        }
                    }
                }

               
                var checkFulfillmentQuery = "SELECT COUNT(1) FROM Product_Warehouse WHERE IdOrder = @OrderId";
                using (var checkFulfillmentCommand = new SqlCommand(checkFulfillmentQuery, connection))
                {
                    checkFulfillmentCommand.Parameters.AddWithValue("@OrderId", orderId);
                    var orderFulfilled = (int)await checkFulfillmentCommand.ExecuteScalarAsync() > 0;

                    if (orderFulfilled)
                    {
                        return BadRequest("Order has already been fulfilled.");
                    }
                }

                
                var updateOrderQuery = "UPDATE [Order] SET FulfilledAt = @FulfilledAt WHERE IdOrder = @OrderId";
                using (var updateOrderCommand = new SqlCommand(updateOrderQuery, connection))
                {
                    updateOrderCommand.Parameters.AddWithValue("@FulfilledAt", DateTime.Now);
                    updateOrderCommand.Parameters.AddWithValue("@OrderId", orderId);
                    await updateOrderCommand.ExecuteNonQueryAsync();
                }

               
                var insertProductWarehouseQuery = @"INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt) 
                                                    VALUES (@WarehouseId, @ProductId, @OrderId, @Amount, @Price, @CreatedAt); 
                                                    SELECT SCOPE_IDENTITY();";
                using (var insertProductWarehouseCommand = new SqlCommand(insertProductWarehouseQuery, connection))
                {
                    insertProductWarehouseCommand.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                    insertProductWarehouseCommand.Parameters.AddWithValue("@ProductId", request.ProductId);
                    insertProductWarehouseCommand.Parameters.AddWithValue("@OrderId", orderId);
                    insertProductWarehouseCommand.Parameters.AddWithValue("@Amount", request.Amount);
                    insertProductWarehouseCommand.Parameters.AddWithValue("@Price", productPrice * request.Amount);
                    insertProductWarehouseCommand.Parameters.AddWithValue("@CreatedAt", DateTime.Now);

                    var newProductWarehouseId = (decimal)await insertProductWarehouseCommand.ExecuteScalarAsync();
                    return Ok(new { IdProductWarehouse = newProductWarehouseId });
                }
            }
        }

        [HttpPost("addProductWithProc")]
        public async Task<IActionResult> AddProductToWarehouseWithProc([FromBody] WarehouseRequest request)
        {
            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than 0.");
            }

            using (var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
            {
                await connection.OpenAsync();

                var command = new SqlCommand("AddProductToWarehouse", connection)
                {
                    CommandType = System.Data.CommandType.StoredProcedure
                };
                command.Parameters.AddWithValue("@ProductId", request.ProductId);
                command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                command.Parameters.AddWithValue("@Amount", request.Amount);
                command.Parameters.AddWithValue("@CreatedAt", request.CreatedAt);

                try
                {
                    var newProductWarehouseId = (decimal)await command.ExecuteScalarAsync();
                    return Ok(new { IdProductWarehouse = newProductWarehouseId });
                }
                catch (SqlException ex)
                {
                    return StatusCode(500, $"Internal server error: {ex.Message}");
                }
            }
        }
    }
}