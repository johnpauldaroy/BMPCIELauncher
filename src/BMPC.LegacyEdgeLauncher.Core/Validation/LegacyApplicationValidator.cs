using BMPC.LegacyEdgeLauncher.Core.Models;
using FluentValidation;

namespace BMPC.LegacyEdgeLauncher.Core.Validation;

/// <summary>FluentValidation rules for registering or editing a legacy application.</summary>
public class LegacyApplicationValidator : AbstractValidator<LegacyApplication>
{
    public LegacyApplicationValidator()
    {
        RuleFor(a => a.Name)
            .NotEmpty().WithMessage("Application name is required.")
            .MaximumLength(200);

        RuleFor(a => a.BaseUrl)
            .NotEmpty().WithMessage("Base URL is required.")
            .Must(url => UrlNormalizer.TryNormalize(url, out _, out _))
            .WithMessage(a =>
            {
                UrlNormalizer.TryNormalize(a.BaseUrl, out _, out var error);
                return error;
            });

        RuleFor(a => a.NormalizedHost)
            .NotEmpty().WithMessage("Normalized host must be computed before saving.");

        RuleFor(a => a.CompatibilityMode)
            .NotEmpty();

        RuleForEach(a => a.NeutralSites).ChildRules(ns =>
        {
            ns.RuleFor(n => n.Url)
                .NotEmpty()
                .Must(url => UrlNormalizer.TryNormalize(url, out _, out _))
                .WithMessage("Neutral site URL is invalid.");
        });
    }
}
