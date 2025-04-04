using System;
using System.Collections.Concurrent;
using API.Data;
using API.DTOs;
using API.Extensions;
using API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly UserManager<AppUser> userManager;
    private readonly AppDbContext context;
    
    public static readonly ConcurrentDictionary<string, OnlineUserDto> onlineUsers = new();
    
    public ChatHub(UserManager<AppUser> userManager, AppDbContext context)
    {
        this.userManager = userManager;
        this.context = context;
    }
    
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var receiverId = httpContext?.Request.Query["senderId"].ToString();
        var userName = Context.User?.Identity?.Name;
        
        if (string.IsNullOrEmpty(userName))
        {
            return;
        }
        
        var currentUser = await userManager.FindByNameAsync(userName);
        if (currentUser == null)
        {
            return;
        }
        
        var connectionId = Context.ConnectionId;
        
        if(onlineUsers.ContainsKey(userName))
        {
            onlineUsers[userName].ConnectionId = connectionId;
        }
        else
        {
            var user = new OnlineUserDto
            {
                ConnectionId = connectionId,
                Username = userName,
                ProfilePicture = currentUser.ProfilePicture,
                FullName = currentUser.FullName
            };
            onlineUsers.TryAdd(userName, user);
            await Clients.AllExcept(connectionId).SendAsync("Notify", currentUser);
        }
        
        if(!string.IsNullOrEmpty(receiverId))
        {
            await LoadMessages(receiverId);
        }
        
        await Clients.All.SendAsync("OnlineUsers", await GetAllUsers());
        
        await base.OnConnectedAsync();
    }
    
    public async Task LoadMessages(string recipientId, int pageNumber = 1)
{
    int pageSize = 10;
    var username = Context.User?.Identity?.Name;
    
    if (string.IsNullOrEmpty(username))
    {
        return;
    }
    
    var currentUser = await userManager.FindByNameAsync(username);
    
    if(currentUser is null)
    {
        return;
    }
    
    // Lấy tin nhắn với sort và pagination tối ưu hơn
    var messagesQuery = context.Messages
        .Where(x => (x.ReceiverId == currentUser.Id && x.SenderId == recipientId) || 
                   (x.SenderId == currentUser.Id && x.ReceiverId == recipientId))
        .OrderByDescending(x => x.CreatedDate);
        
    var totalMessages = await messagesQuery.CountAsync();
    
    var messages = await messagesQuery
        .Skip((pageNumber - 1) * pageSize)
        .Take(pageSize)
        .OrderBy(x => x.CreatedDate)
        .Select(x => new MessageResponseDto
        {
            Id = x.Id,
            Content = x.Content,
            CreateDated = x.CreatedDate,
            ReceiverId = x.ReceiverId, 
            SenderId = x.SenderId
        })
        .ToListAsync();
        
    // Cập nhật trạng thái đã đọc một lần và save
    var unreadMessageIds = messages
        .Where(m => m.ReceiverId == currentUser.Id)
        .Select(m => m.Id)
        .ToList();
        
    if (unreadMessageIds.Any())
    {
        await context.Messages
            .Where(m => unreadMessageIds.Contains(m.Id))
            .ForEachAsync(m => m.IsRead = true);
            
        await context.SaveChangesAsync();
    }
    
    // Trả về kết quả với thông tin bổ sung về trang
    await Clients.Client(Context.ConnectionId).SendAsync("ReceiveMessageList", messages);
}

// Cải thiện SendMessage để trả về ID tin nhắn mới
public async Task<int> SendMessage(MessageRequestDto message)
{
    var username = Context.User?.Identity?.Name;
    
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(message.ReceiverId))
    {
        return 0; // Trả về 0 nếu không thành công
    }
    
    var senderUser = await userManager.FindByNameAsync(username);
    var recipientUser = await userManager.FindByIdAsync(message.ReceiverId);
    
    if (senderUser == null || recipientUser == null)
    {
        return 0;
    }

    var newMsg = new Message
    {
        SenderId = senderUser.Id,
        ReceiverId = recipientUser.Id,
        IsRead = false,
        CreatedDate = DateTime.UtcNow,
        Content = message.Content
    };
    
    context.Messages.Add(newMsg);
    await context.SaveChangesAsync();
    
    // Trả về ID tin nhắn mới tạo
    var messageResponse = new MessageResponseDto
    {
        Id = newMsg.Id,
        Content = newMsg.Content,
        CreateDated = newMsg.CreatedDate,
        ReceiverId = newMsg.ReceiverId,
        SenderId = newMsg.SenderId
    };
    
    // Gửi tin nhắn đến người nhận nếu họ online
    if (onlineUsers.TryGetValue(recipientUser.UserName ?? string.Empty, out var onlineUser) &&
        !string.IsNullOrEmpty(onlineUser.ConnectionId))
    {
        await Clients.Client(onlineUser.ConnectionId).SendAsync("ReceiveNewMessage", messageResponse);
    }
    
    return newMsg.Id;
}

    public async Task NotifyTyping(string recipientUsername)
    {
        var senderUserName = Context.User?.Identity?.Name;
        if(string.IsNullOrEmpty(senderUserName))
        {
            return;
        }
        
        var connectionId = onlineUsers.Values.FirstOrDefault(x => x.Username == recipientUsername)?.ConnectionId;
        if(!string.IsNullOrEmpty(connectionId))
        {
            await Clients.Client(connectionId).SendAsync("NotifyTypingToUser", senderUserName);
        }
    }
    
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var username = Context.User?.Identity?.Name;
        if (!string.IsNullOrEmpty(username))
        {
            onlineUsers.TryRemove(username, out _);
            await Clients.All.SendAsync("OnlineUsers", await GetAllUsers());
        }
        
        await base.OnDisconnectedAsync(exception);
    }
    
    private async Task<IEnumerable<OnlineUserDto>> GetAllUsers()
    {
        var username = Context.User?.Identity?.Name;
        
        if (string.IsNullOrEmpty(username))
        {
            return Enumerable.Empty<OnlineUserDto>();
        }
        
        var onlineUsersSet = new HashSet<string>(onlineUsers.Keys);
        
        var users = await userManager.Users
            .Select(u => new OnlineUserDto
            {
                Id = u.Id,
                Username = u.UserName,
                FullName = u.FullName,
                ProfilePicture = u.ProfilePicture,
                IsOnline = onlineUsersSet.Contains(u.UserName ?? string.Empty),
                UnreadCount = context.Messages.Count(x => x.ReceiverId == username && x.SenderId == u.Id && !x.IsRead)
            })
            .OrderByDescending(u => u.IsOnline)
            .ToListAsync();
            
        return users;
    }
}