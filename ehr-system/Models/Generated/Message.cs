using System;

namespace EHR.Models.Generated;

/// <summary>
/// Message types for the internal messaging system.
/// </summary>
public enum MessageType
{
    Text = 0,
    VoiceNote = 1,
    File = 2
}

/// <summary>
/// Represents a message in a conversation.
/// Message content is encrypted for HIPAA compliance.
/// </summary>
public partial class Message
{
    public int MessageId { get; set; }

    public int TenantId { get; set; }

    public int ConversationId { get; set; }

    public int SenderId { get; set; }

    public int RecipientId { get; set; }

    /// <summary>
    /// The message text content (encrypted at rest for HIPAA compliance).
    /// For voice notes and files, this may contain a caption or description.
    /// </summary>
    public string? MessageText { get; set; }

    /// <summary>
    /// Type of message: Text, VoiceNote, or File
    /// </summary>
    public int MessageType { get; set; }

    /// <summary>
    /// URL/path to the file in cloud storage (for voice notes and file attachments).
    /// Files are encrypted at rest.
    /// </summary>
    public string? FileUrl { get; set; }

    /// <summary>
    /// Original filename (for display purposes)
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long? FileSize { get; set; }

    /// <summary>
    /// MIME type of the file (e.g., "audio/webm", "application/pdf")
    /// </summary>
    public string? FileMimeType { get; set; }

    /// <summary>
    /// Duration in seconds for voice notes
    /// </summary>
    public decimal? FileDurationSeconds { get; set; }

    /// <summary>
    /// Whether the message has been read by the recipient
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>
    /// Timestamp when the message was read
    /// </summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>
    /// Soft delete: message hidden from sender's view
    /// </summary>
    public bool IsDeletedBySender { get; set; }

    /// <summary>
    /// Soft delete: message hidden from recipient's view
    /// </summary>
    public bool IsDeletedByRecipient { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Tenant Tenant { get; set; } = null!;
    public virtual Conversation Conversation { get; set; } = null!;
    public virtual User Sender { get; set; } = null!;
    public virtual User Recipient { get; set; } = null!;
}
