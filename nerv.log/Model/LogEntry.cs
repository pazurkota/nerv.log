using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace nerv.log.Model;

public class LogEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }
    
    [Column("timestamp")]
    public DateTime TimeStamp { get; set; } = DateTime.UtcNow;

    [Column("level")] 
    [StringLength(50)]
    public string Level { get; set; } = string.Empty;

    [Column("service_name")]
    [StringLength(255)]
    public string ServiceName { get; set; } = string.Empty;

    [Column("message")]
    public string Message { get; set; } = string.Empty;
}