namespace JobRecruitment.Models
{
    public class PhotoOrderDto
    {
        public string Id { get; set; }
        public int Order { get; set; }
    }

    public class UploadPhotoDto
    {
        public IFormFile File { get; set; }
        public string PhotoType { get; set; }
        public string Caption { get; set; }
    }
}