using System.ComponentModel.DataAnnotations;

namespace PgFreshCache.TestApi.Model
{
    public class Store
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }

        [MaxLength(200)]
        public string? Address { get; set; }

        public ICollection<Stock> Stocks { get; set; }

        public Store(int id, string name, string address)
        {
            Id = id;
            Name = name;
            Address = address;
            Stocks = [];
        }
    }
}