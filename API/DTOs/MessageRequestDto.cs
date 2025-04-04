using System;
namespace API.DTOs;
public class MessageRequestDto
{
    public int Id {get; set; }
    public string? SenderId{get; set; }
    public string? ReceiverId{get; set; }
    public string? Content{get; set; }
    public bool IsRead {get; set; }
    public DateTime CreateDated {get; set; }
    public string? SenderUsername { get; set; } 
    public string? ReceiverUsername { get; set; } 
}