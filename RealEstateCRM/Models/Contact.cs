namespace RealEstateCRM.Models
{
    public class Contact
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public string Agent { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}