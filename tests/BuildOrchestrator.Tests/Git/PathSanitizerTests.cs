using System;
using System.Collections.Generic;
using BuildOrchestrator.Core.Git;
using Xunit;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [T13] PathSanitizer: branch → filesystem-güvenli slug, auto worktree adı (v7 A6), tek-segment güvenlik
/// validasyonu. Saf string mantığı — git çağrısı/I/O yok; tüm testler senkron ve deterministik.
/// </summary>
public class PathSanitizerTests
{
    // ---- SanitizeBranchSlug ----

    [Fact] // brief örneği: '/' → '-'
    public void SanitizeBranchSlug_replaces_forward_slash_with_dash()
    {
        Assert.Equal("release-2026.06", PathSanitizer.SanitizeBranchSlug("release/2026.06"));
    }

    [Fact] // brief örneği: hem '/' hem '\' aynı branch içinde normalize edilir
    public void SanitizeBranchSlug_replaces_both_slash_and_backslash_with_dash()
    {
        Assert.Equal("feature-x-y", PathSanitizer.SanitizeBranchSlug("feature/x\\y"));
    }

    [Fact] // ardışık ayraçlar tek '-' üretmeli (çift '-' değil), baştaki/sondaki '-' kırpılır
    public void SanitizeBranchSlug_collapses_consecutive_separators_and_trims_dashes()
    {
        Assert.Equal("a-b", PathSanitizer.SanitizeBranchSlug("/a//b\\"));
    }

    [Theory] // geçersiz Windows dosya-adı karakterleri temizlenir (rejekte değil — best-effort slug üretimi)
    [InlineData("feat:x", "feat-x")]
    [InlineData("a*b", "a-b")]
    [InlineData("a?b", "a-b")]
    [InlineData("a\"b", "a-b")]
    [InlineData("a<b>c", "a-b-c")]
    [InlineData("a|b", "a-b")]
    public void SanitizeBranchSlug_replaces_invalid_windows_chars_with_dash(string input, string expected)
    {
        Assert.Equal(expected, PathSanitizer.SanitizeBranchSlug(input));
    }

    [Fact] // mutlak yol enjeksiyonu (sürücü harfi + ':' + '\') tek güvenli segmente indirgenir
    public void SanitizeBranchSlug_neutralizes_absolute_path_injection()
    {
        string slug = PathSanitizer.SanitizeBranchSlug(@"C:\Users\evil");
        Assert.Equal("C-Users-evil", slug);
        Assert.True(PathSanitizer.IsSafeSegment(slug)); // sanitize sonrası güvenli tek segment olmalı
    }

    [Fact] // path-traversal enjeksiyonu ("../../etc") ayraçsız tek segmente indirgenir — artık gerçek traversal değil
    public void SanitizeBranchSlug_neutralizes_path_traversal_injection()
    {
        string slug = PathSanitizer.SanitizeBranchSlug("../../etc/passwd");
        Assert.Equal("..-..-etc-passwd", slug);
        Assert.DoesNotContain('/', slug);
        Assert.DoesNotContain('\\', slug);
    }

    [Theory] // sanitize sonrası TAM olarak ".." veya "." kalan sonuç — sessizce traversal adı üretmek yerine reddedilir
    [InlineData("..")]
    [InlineData(".")]
    public void SanitizeBranchSlug_throws_when_result_is_exactly_dot_or_dotdot(string branch)
    {
        Assert.Throws<ArgumentException>(() => PathSanitizer.SanitizeBranchSlug(branch));
    }

    [Theory] // boş/whitespace branch → tanımlı davranış: ArgumentException (sessiz fallback yerine net hata)
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SanitizeBranchSlug_throws_on_empty_or_whitespace_branch(string? branch)
    {
        Assert.Throws<ArgumentException>(() => PathSanitizer.SanitizeBranchSlug(branch!));
    }

    // ---- NextWorktreeName ----

    [Fact] // v7 A6 örneği birebir: "main" + 1 mevcut (bizzat "main") → "main-2"
    public void NextWorktreeName_returns_main_2_when_one_existing_matches()
    {
        Assert.Equal("main-2", PathSanitizer.NextWorktreeName("main", new[] { "main" }));
    }

    [Fact] // hiç mevcut yokken formül literal uzantısı: sayı = 0+1 = 1 → "main-1"
    public void NextWorktreeName_returns_slug_1_when_none_existing()
    {
        Assert.Equal("main-1", PathSanitizer.NextWorktreeName("main", Array.Empty<string>()));
    }

    [Fact] // iki eşleşen mevcut ("main" + "main-2") → sıradaki "main-3"
    public void NextWorktreeName_increments_past_existing_numbered_variants()
    {
        Assert.Equal("main-3", PathSanitizer.NextWorktreeName("main", new[] { "main", "main-2" }));
    }

    [Fact] // eşleşme case-insensitive (Windows dosya sistemi ile tutarlı)
    public void NextWorktreeName_matching_is_case_insensitive()
    {
        Assert.Equal("main-2", PathSanitizer.NextWorktreeName("main", new[] { "MAIN" }));
    }

    [Fact] // alakasız ad ("main-experimental") sayısal suffix kalıbına uymadığı için SAYILMAZ
    public void NextWorktreeName_ignores_unrelated_names_sharing_textual_prefix_but_not_numeric_suffix()
    {
        Assert.Equal("main-1", PathSanitizer.NextWorktreeName("main", new[] { "main-experimental" }));
    }

    [Fact] // branch slug'ı '/' içerse bile isim türetimi ve eşleştirme slug üzerinden çalışır
    public void NextWorktreeName_uses_sanitized_slug_for_branch_with_slash()
    {
        Assert.Equal("release-2026.06-2", PathSanitizer.NextWorktreeName("release/2026.06", new[] { "release-2026.06" }));
    }

    [Fact] // existing listesindeki bambaşka bir slug'a ait adlar sayıma dahil edilmez
    public void NextWorktreeName_does_not_count_names_from_a_different_slug()
    {
        Assert.Equal("main-1", PathSanitizer.NextWorktreeName("main", new[] { "release-2026.06", "release-2026.06-2" }));
    }

    // ---- IsSafeSegment ----

    [Theory]
    [InlineData("main")]
    [InlineData("main-2")]
    [InlineData("release-2026.06")]
    public void IsSafeSegment_returns_true_for_clean_segments(string segment)
    {
        Assert.True(PathSanitizer.IsSafeSegment(segment));
    }

    [Theory] // reddedilen girdi: ".." / "." / mutlak yol / geçersiz Windows karakteri / boş
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(@"C:\Users\evil")]
    [InlineData(@"\Users\evil")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSafeSegment_returns_false_for_unsafe_input(string segment)
    {
        Assert.False(PathSanitizer.IsSafeSegment(segment));
    }

    [Fact] // null girdi de false dönmeli (throw değil) — validator asla fırlamaz
    public void IsSafeSegment_returns_false_for_null_without_throwing()
    {
        Assert.False(PathSanitizer.IsSafeSegment(null!));
    }
}
