using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

/// <summary>
/// Metadata for one intake session. Never the data itself — actual clinical rows
/// live in the normal clinical tables tagged with this submission's Id via IntakeSubmissionId.
/// </summary>
public partial class PatientIntakeSubmission
{
    public int PatientIntakeSubmissionId { get; set; }

    public int PatientId { get; set; }

    public int TenantId { get; set; }

    /// <summary>
    /// When the patient first touched any field in this session.
    /// </summary>
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// When the patient clicked final Submit. Null = still in progress.
    /// </summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>
    /// JSON array of section names touched in this session,
    /// e.g. ["demographics","concerns","meds"].
    /// </summary>
    public string SectionsTouched { get; set; }

    /// <summary>
    /// 0=Portal, 1=Tablet (see IntakeChannel enum).
    /// </summary>
    public int SourceChannel { get; set; }

    public string IpAddress { get; set; }

    public string UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// Section 2 free-text: "When did you last feel well?"
    /// </summary>
    public string LastFeltWell { get; set; }

    /// <summary>
    /// Section 2 free-text: "What triggered your health change?"
    /// </summary>
    public string WhatTriggered { get; set; }

    /// <summary>
    /// Section 2 free-text: "What makes your symptoms BETTER?"
    /// </summary>
    public string BetterFactors { get; set; }

    /// <summary>
    /// Section 2 free-text: "What makes your symptoms WORSE?"
    /// </summary>
    public string WorseFactors { get; set; }

    /// <summary>
    /// Section 2 free-text: "Additional Health History &amp; Timeline (significant events, past illnesses, injuries)"
    /// </summary>
    public string AdditionalTimeline { get; set; }

    /// <summary>
    /// Section 3 free-text: "Cancer — specify type(s) and year(s)"
    /// </summary>
    public string CancerSpecify { get; set; }

    /// <summary>
    /// Section 5 (Lifestyle) structured choices as JSON. Pre-fills the wizard
    /// on return visits so patient sees their previous answers.
    /// Shape: { sleep: {hours, quality, issues[]}, exercise: {...}, diet: {...}, ... }
    /// </summary>
    public string LifestyleData { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Tenant Tenant { get; set; }
}
