using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using traveltest.Models;

namespace traveltest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CommentsController : ControllerBase
    {
        private readonly string _connectionString;
        private readonly string _uploadFolder;
        private readonly string[] _randomAvatars = new[]
        {
            "https://picsum.photos/id/237/100/100",
            "https://picsum.photos/id/1005/100/100",
            "https://picsum.photos/id/1012/100/100",
            "https://picsum.photos/id/1025/100/100",
            "https://picsum.photos/id/1074/100/100"
        };

        public CommentsController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
            _uploadFolder = Path.Combine(environment.WebRootPath, "uploads");
            Directory.CreateDirectory(_uploadFolder);
        }

        [HttpGet]
        public async Task<ActionResult<PaginationResult<Comments>>> GetComments(
            string search = "",
            string category = "",
            string sort = "createdDate_desc",
            int page = 1,
            int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 50) pageSize = 10;

            var comments = new List<Comments>();
            int totalItems = 0;

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // 构建查询条件
                var conditions = new List<string>();
                var parameters = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(search))
                {
                    conditions.Add("Title LIKE @Search OR Content LIKE @Search");
                    parameters.Add("@Search", $"%{search}%");
                }

                var whereClause = conditions.Any() ? "WHERE " + string.Join(" AND ", conditions) : "";

                // 查询总记录数
                using (var countCommand = new MySqlCommand(
                    $"SELECT COUNT(*) FROM Comments {whereClause}",
                    connection))
                {
                    foreach (var param in parameters)
                    {
                        countCommand.Parameters.AddWithValue(param.Key, param.Value);
                    }
                    totalItems = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                }

                // 构建排序条件
                var orderBy = "Createtime DESC";
                if (sort == "createdDate_asc") orderBy = "Createtime ASC";
                else if (sort == "star_desc") orderBy = "Star DESC";
                else if (sort == "star_asc") orderBy = "Star ASC";

                // 查询当前页数据
                using (var selectCommand = new MySqlCommand(
                    $"SELECT * FROM Comments {whereClause} ORDER BY {orderBy} LIMIT @Offset, @PageSize",
                    connection))
                {
                    foreach (var param in parameters)
                    {
                        selectCommand.Parameters.AddWithValue(param.Key, param.Value);
                    }
                    selectCommand.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
                    selectCommand.Parameters.AddWithValue("@PageSize", pageSize);

                    using (var reader = await selectCommand.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            comments.Add(MapCommentFromReader(reader));
                        }
                    }
                }
            }

            var totalPages = (int)Math.Ceiling((double)totalItems / pageSize);

            return Ok(new PaginationResult<Comments>
            {
                Items = comments,
                TotalPages = totalPages,
                CurrentPage = page,
                PageSize = pageSize,
                TotalItems = totalItems
            });
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Comments>> GetComment(int id)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new MySqlCommand(
                    "SELECT * FROM Comments WHERE Id = @Id",
                    connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return Ok(MapCommentFromReader(reader));
                        }
                        else
                        {
                            return NotFound();
                        }
                    }
                }
            }
        }

        [HttpPost]
        public async Task<ActionResult<Comments>> PostComment([FromForm] CommentsCreateModel model)
        {
            string imgUrl = null;
            if (model.Imgfile != null && model.Imgfile.Length > 0)
            {
                try
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.Imgfile.FileName);
                    var filePath = Path.Combine(_uploadFolder, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.Imgfile.CopyToAsync(stream);
                    }
                    imgUrl = $"/uploads/{fileName}";
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"上传图片失败: {ex.Message}");
                }
            }
            else
            {
                // 随机选择一个头像
                var random = new Random();
                imgUrl = _randomAvatars[random.Next(0, _randomAvatars.Length)];
            }

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (var insertCommand = new MySqlCommand(
                            @"INSERT INTO Comments (Title, Createtime, Author, Star, Content, Img) 
                              VALUES (@Title, @Createtime, @Author, @Star, @Content, @Img); 
                              SELECT LAST_INSERT_ID();",
                            connection,
                            transaction))
                        {
                            insertCommand.Parameters.AddWithValue("@Title", model.Title);
                            insertCommand.Parameters.AddWithValue("@Createtime", DateTime.UtcNow);
                            insertCommand.Parameters.AddWithValue("@Author", model.Author);
                            insertCommand.Parameters.AddWithValue("@Star", model.Star);
                            insertCommand.Parameters.AddWithValue("@Content", model.Content);
                            insertCommand.Parameters.AddWithValue("@Img", imgUrl);

                            int commentId = Convert.ToInt32(await insertCommand.ExecuteScalarAsync());
                            transaction.Commit();
                            return CreatedAtAction(nameof(GetComment), new { id = commentId }, await GetCommentById(commentId));
                        }
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return StatusCode(500, $"创建评论失败: {ex.Message}");
                    }
                }
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Comments>> UpdateComment(int id, [FromForm] CommentsUpdateModel model)
        {
            Console.WriteLine($"Model Id: {model.Id}, Title: {model.Title}, Star: {model.Star}");
            if (id != model.Id)
            {
                return BadRequest("请求路径中的ID与请求体中的ID不一致");
            }

            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    if (!CommentExists(id, connection))
                    {
                        return NotFound();
                    }

                    string imgUrl = null;
                    if (model.Imgfile != null && model.Imgfile.Length > 0)
                    {
                        try
                        {
                            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(model.Imgfile.FileName);
                            var filePath = Path.Combine(_uploadFolder, fileName);
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await model.Imgfile.CopyToAsync(stream);
                            }
                            imgUrl = $"/uploads/{fileName}";
                        }
                        catch (Exception ex)
                        {
                            return StatusCode(500, $"上传图片失败: {ex.Message}");
                        }
                    }
                    else
                    {
                        imgUrl = GetOldImgUrl(id, connection);
                    }

                    using (var updateCommand = new MySqlCommand(
                        @"UPDATE Comments 
                  SET Title = @Title, 
                      Createtime = @Createtime,  -- 如果不想更新创建时间，可以移除这一行
                      Author = @Author, 
                      Star = @Star, 
                      Content = @Content, 
                      Img = @Img 
                  WHERE Id = @Id",
                        connection))
                    {
                        updateCommand.Parameters.AddWithValue("@Title", model.Title);
                        // 如果选择方法二，使用当前时间而不是模型中的值
                        updateCommand.Parameters.AddWithValue("@Createtime", DateTime.UtcNow);
                        updateCommand.Parameters.AddWithValue("@Author", model.Author);
                        updateCommand.Parameters.AddWithValue("@Star", model.Star);
                        updateCommand.Parameters.AddWithValue("@Content", model.Content);
                        updateCommand.Parameters.AddWithValue("@Img", imgUrl);
                        updateCommand.Parameters.AddWithValue("@Id", id);

                        int rowsAffected = await updateCommand.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                        {
                            return NotFound();
                        }

                        return Ok(await GetCommentById(id));
                    }
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"更新评论失败: {ex.Message}");
                }
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteComment(int id)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                try
                {
                    if (!CommentExists(id, connection))
                    {
                        return NotFound();
                    }

                    string oldImgUrl = GetOldImgUrl(id, connection);

                    using (var deleteCommand = new MySqlCommand(
                        "DELETE FROM Comments WHERE Id = @Id",
                        connection))
                    {
                        deleteCommand.Parameters.AddWithValue("@Id", id);
                        int rowsAffected = await deleteCommand.ExecuteNonQueryAsync();

                        if (rowsAffected == 0)
                        {
                            return NotFound();
                        }
                    }

                    if (!string.IsNullOrEmpty(oldImgUrl) && !oldImgUrl.StartsWith("https://picsum.photos"))
                    {
                        DeleteOldImage(oldImgUrl);
                    }

                    return NoContent();
                }
                catch (Exception ex)
                {
                    return StatusCode(500, $"删除评论失败: {ex.Message}");
                }
            }
        }

        private string GetOldImgUrl(int id, MySqlConnection connection)
        {
            using (var command = new MySqlCommand(
                "SELECT Img FROM Comments WHERE Id = @Id",
                connection))
            {
                command.Parameters.AddWithValue("@Id", id);
                return (string)command.ExecuteScalar() ?? "";
            }
        }

        private void DeleteOldImage(string oldImgUrl)
        {
            if (!string.IsNullOrEmpty(oldImgUrl) && !oldImgUrl.StartsWith("https://picsum.photos"))
            {
                var fileName = oldImgUrl.Split('/').Last();
                var filePath = Path.Combine(_uploadFolder, fileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
        }

        private bool CommentExists(int id, MySqlConnection connection)
        {
            using (var command = new MySqlCommand(
                "SELECT COUNT(*) FROM Comments WHERE Id = @Id",
                connection))
            {
                command.Parameters.AddWithValue("@Id", id);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private Comments MapCommentFromReader(MySqlDataReader reader)
        {
            return new Comments
            {
                Id = reader.GetInt32("Id"),
                Title = reader.GetString("Title"),
                Createtime = reader.GetDateTime("Createtime"),
                Author = reader.GetString("Author"),
                Star = reader.GetInt32("Star"),
                Content = reader.GetString("Content"),
                Img = reader.IsDBNull(reader.GetOrdinal("Img")) ?
                    "https://picsum.photos/100/100" : reader.GetString("Img"),
            };
        }

        private async Task<Comments> GetCommentById(int id)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = new MySqlCommand(
                    "SELECT * FROM Comments WHERE Id = @Id",
                    connection))
                {
                    command.Parameters.AddWithValue("@Id", id);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return MapCommentFromReader(reader);
                        }
                    }
                }
            }

            return null;
        }
    }
}