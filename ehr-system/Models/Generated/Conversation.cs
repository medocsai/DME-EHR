using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Represents a conversation between two users within the same tenant.
/// Used for the internal messaging system.
/// </summary>
public partial class Conversation
{
    public int ConversationId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// First participant in the conversation (lower UserId stored here for consistency)
    /// </summary>
    public int User1Id { get; set; }

    /// <summary>
    /// Second participant in the conversation (higher UserId stored here for consistency)
    /// </summary>
    public int User2Id { get; set; }

    /// <summary>
    /// Preview text from the last message (encrypted, truncated to 500 chars)
    /// </summary>
    public string? LastMessageText { get; set; }

    /// <summary>
    /// Timestamp of the last message
    /// </summary>
    public DateTime? LastMessageAt { get; set; }

    /// <summary>
    /// User ID of who sent the last message
    /// </summary>
    public int? LastMessageSenderId { get; set; }

    /// <summary>
    /// Number of unread messages for User1
    /// </summary>
    public int User1UnreadCount { get; set; }

    /// <summary>
    /// Number of unread messages for User2
    /// </summary>
    public int User2UnreadCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual User User1 { get; set; } = null!;
    public virtual User User2 { get; set; } = null!;
    public virtual User? LastMessageSender { get; set; }
    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
