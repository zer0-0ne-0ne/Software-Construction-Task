using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace traveltest.Models
{
    public class Comments
    {
        public int Id { get; set; }
        public DateTime Createtime { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public int Star { get; set; }
        public string Content { get; set; }
        public string Img { get; set; }
    }

    public class CommentsCreateModel
    {
        [Required(ErrorMessage = "标题不能为空")]
        public string Title { get; set; }

        [Required(ErrorMessage = "作者不能为空")]
        public string Author { get; set; }

        [Required(ErrorMessage = "评分不能为空")]
        [Range(1, 5, ErrorMessage = "评分必须在1-5之间")]
        public int Star { get; set; }

        [Required(ErrorMessage = "内容不能为空")]
        public string Content { get; set; }

        public IFormFile Imgfile { get; set; }
    }

    public class CommentsUpdateModel
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "标题不能为空")]
        public string Title { get; set; }

        [Required(ErrorMessage = "作者不能为空")]
        public string Author { get; set; }

        [Required(ErrorMessage = "评分不能为空")]
        [Range(1, 5, ErrorMessage = "评分必须在1-5之间")]
        public int Star { get; set; }

        [Required(ErrorMessage = "内容不能为空")]
        public string Content { get; set; }

        public IFormFile Imgfile { get; set; }
    }

    public class PaginationResult<T>
    {
        public List<T> Items { get; set; }
        public int TotalPages { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
    }
}