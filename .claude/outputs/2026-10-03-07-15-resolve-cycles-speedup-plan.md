# Resolve cycles hızlandırma — uygulama planı

> **Uygulayıcı için (zorunlu alt beceri):** bu plan `superpowers:subagent-driven-development` (önerilen) ya da
> `superpowers:executing-plans` ile **task task** uygulanır. Adımlar `- [ ]` kutularıyla izlenir. Aynı anda en
> çok **3 ajan** (kullanıcının oturum limiti); ölçüm ve veri toplama ajanla değil script'le yapılır.

**Tarih:** 2026-10-03 · **Taban:** `develop` @ `8da8b4f` · **Spec (ölçüm raporu):**
`.claude/outputs/2026-10-02-22-15-cycle-resolve-performance-analysis.md` (B1–B8 bulguları, Ö1–Ö6 önerileri,
Ek — 2026-10-03) ve denetçi notları `.claude/temp/cycle-resolve-perf-2026-10-02/agents/review.md` (K1–K13,
W1–W14). Plan bu iki belgeden argüman kurar; uygulayıcı ikisini de okur.

**Amaç:** "Resolve cycles" süresini, çıktıları değiştirmeden, üç ölçülmüş kaldıraçla kısaltmak: (1) döngü
üyelerinden yalnız GEREKENİ derlemek (tek üye değişiminde 63 sn → ~6–8 sn), (2) WPF'in geçici assembly'sini
metadata-only derlemek (her senaryoda −%18…−24), (3) isteğe bağlı: Cycles koşusunu yük altında aç bırakmamak
(yük altında 119 → 80 sn). Ön koşul: yüzey özetinin ölçülmüş kör noktalarını kapatmak.

**Mimari (iki cümle):** Tur döngüsünün kanıt standardı değişmez — "kaynak sabitken bir üyenin sonucunu yalnız
okuduğu yüzeyin değişmesi değiştirir" — yalnızca kanıt KOŞULAR ARASINA taşınır: her üyenin kendi terimi, okuduğu
yüzeyler, çıktı kanıtı ve motor parmak izi deftere yazılır; sonraki koşuda tur 1 bu kayıtla "gerekli" olmayan
üyeyi derlemez, tur sonu kuralı (`CycleRoundPolicy`) aynen kalır. WPF geçici assembly'si araçla gelen küçük bir
targets dosyasıyla gövdesiz derlenir; nihai çıktı dokunulmaz.

**Teknoloji:** .NET 10, xUnit (`[Fact]`, `[StaFact]`, `[SkippableFact]`), `System.Reflection.Metadata`,
`System.Reflection.Emit.PersistedAssemblyBuilder` (test PE'leri), MSBuild.exe (VS 18.9; VS 2022 doğrulandı),
ölçüm harness'i `.claude/temp/cycle-resolve-perf-2026-10-02/` (git'e girmez; aynı makinede durur).

---

## 0. Küresel kısıtlar (repo `CLAUDE.md`; her task'ın örtük gereksinimi)

- **Kırmızı test kuralı:** fix, kusuru yakalayan test KIRMIZI verdiği gösterilmeden yapılmaz. Kırmızıyı
  gösteremiyorsan test yanlıştır.
- **Davranış değişince testi de değişir:** eski kuralı pinleyen test silinmez/gevşetilmez; yeni kuralı pinler,
  doc'una eski iddia + gerekçe (ölçüm) yazılır. Eşik gevşetmek YASAK.
- **Kopya YASAK:** aynı değer/metin/primitif iki yerde tanımlanmaz (kod ve test; ortak fixture tek yerde).
- **Değişmezler:** in-process MSBuild yok (her proje `MSBuild.exe` child'ı); nested Job Object; **OutDir'e ve obj
  düzenine dokunulmaz** (bu planın targets dosyası hiçbir çıktı yolunu değiştirmez, yalnız WPF'in silip attığı
  ara assembly'yi gövdesiz yapar); git'e araç yazmaz; stdout yalnız NDJSON; planlama Core'da; Velopack yalnız App.
- **Doküman aynı işte güncellenir;** anlatı üslubu ("şu oturumda şunu yaptık" YOK); bayatlayacak rakam gömme yok.
- **Kod, UI metinleri, loglar İngilizce; yorumlar ve `.claude/` kayıtları Türkçe.**
- **Attribution satırı HİÇBİR YERE eklenmez** (commit, PR, dosya).
- **Build/test:** `dotnet build BuildOrchestrator.slnx` ·
  `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Uygulama açıkken build alınmaz; gerekiyorsa `-c Release`. Ölçüm testi = ortam değişkeni kapısı
  (`[SkippableFact]` + `Skip.IfNot(... == "1")`).
- **Git:** faz başına `develop`'tan branch; task başına commit; faz bitince `develop`'a `--no-ff` merge + push;
  merge doğrulanınca branch local'den silinir (push edilmemiş branch'in remote'u yoktur). `main`'e yalnız
  `/release`. Oturum `develop` üzerinde bitirilir. Sürüm numarası ve CHANGELOG'a dokunulmaz.
- **Pano:** her task başında/sonunda `status.ps1 -Event steps` (global CLAUDE.md "İlerleme İzlenebilirliği").
- **Kullanıcının OSYS çalışma kopyası canlıdır.** Gerçek repoda ölçüm yapan her adım `snap.py save` ile başlar,
  `snap.py restore` + `verify` (0 fark) ile biter; uygulama kapalı olmalı; `Directory.Build.rsp` geçici
  konur ve KALDIRILIR (bkz. §5 ölçüm).

## 1. Diğer planla ilişki (`.claude/outputs/2026-10-03-04-28-performance-implementation-plan.md`)

O plan tepside derleme, motor IO/bellek, işçi bütçesi ve **Faz E** (E1 Resolve karar logu, E2 yüzey hash'i
paralel + slot dışı, E3 koşullu restore, E4 ölçüm) içerir ve karar 14 ile "üye düzeyi artımlılık" ile
`UseSharedCompilation`'ı kapsam dışı bırakır. **Bu plan tam o boşluğu doldurur** ve E1–E3'ü TEKRARLAMAZ.

- Bu planın **Faz 1** (yüzey özeti) ve **Faz 2** (WPF targets) o planla çakışmaz; herhangi bir sırada yapılır.
- **Faz 3** (üye düzeyi atlama) `RunCoordinator.BuildCycleGroupAsync`'in E1/E2'nin de değiştirdiği bölgelerine
  dokunur. Sıra: **önce o planın Faz E'si merge olur, sonra bu planın Faz 3'ü** (ya da tersi — ama ikisi aynı anda
  açık branch'lerde yürütülmez).
- O planın E4'ü ("kaç üyenin içeriği değişmişti" ölçümü) bu raporla cevaplanmıştır (B1: hareketlerin %40'ı tek
  üye); E4 atlanabilir.

## 2. Karar seti (sabit — plan bunları uygular; tartışma yeniden açılmaz)

| # | Konu | Karar |
|---|---|---|
| 1 | Yüzey özeti kapsamı | Roslyn reference-assembly yüzeyiyle hizalanır: parametre / dönüş / generic parametre öznitelikleri girer; **değer tiplerinin TÜM alanları** (private dahil) girer; `ClassLayout` (Size/Pack) girer. Internal üyeler dahil kalır (IVT muhafazakârlığı). |
| 2 | Üye düzeyi atlama kuralı | Üye "gerekli" sayılır ⇔ (i) terimi değişmiş **veya** (ii) kayıtlı okuduğu bir yüzey artık farklı **veya** (iii) güvenilir kaydı / yüzey kanıtı yok **veya** (iv) kendi çıktı kanıtı eksik ya da bozuk **veya** (v) çıktı "dışarıda derlenmiş" kipinde **veya** (vi) motor parmak izi farklı. Gerekli olmayan üye derlenmez, `skipped — up to date` raporlanır, defterine YENİ bileşik imza yazılır. |
| 3 | Tur sonu kuralı | DEĞİŞMEZ (`CycleRoundPolicy.Decide`). Atlanan üye, kayıtlı yüzeyleriyle "bayat mı?" sorusuna her tur sonunda girer; bayatlarsa sonraki turda derlenir. |
| 4 | Yakınsamayan / kesilen koşu | Bugünkü gibi hiçbir şey persist edilmez; yeni alanlar da yalnız `Converged`'de yazılır. |
| 5 | Atlanan üyenin bin'indeki kardeş kopyaları | TAZELENMEZ; dokümana yazılır (OSYS ortak `Bin`'den çalışır). |
| 6 | Parmak izi | `MSBuild.exe` tam yolu + dosya sürümü + argüman sözleşmesinin (`MsBuildArguments.Build` çıktısının proje yolu dışındaki kısmı) SHA-256'sı. Farklıysa grupta herkes gerekli. |
| 7 | WPF targets içeriği | Üç öğe: `ProduceOnlyReferenceAssembly=true`, `ProduceReferenceAssembly=false`, geçici assembly'ye `InternalsVisibleTo` taşıyan tek kaynak dosyası. Yalnız `_wpftmp` ile biten proje adında. |
| 8 | Targets dosyasının yeri | Motorun önbellek kökü (`build-state.json`'un klasörü) altında `msbuild\wpf-temporary-assembly.targets` + `wpf-temporary-assembly-friend.cs`; motor açılışta içerik farklıysa yazar (sürümsüz, kalıcı yol; `--logs` ile yalıtılan test motoru kendi kökünü kullanır). |
| 9 | Targets'ın komut satırına girişi | `MsBuildArguments.Build` listesine `-p:CustomBeforeMicrosoftCommonTargets=<yol>` eklenir; Build/Rebuild/Clean üçünde de (Clean'de geçici proje doğmaz, zararsız; sözleşme tek kalır). Restore çağrısına girmez. |
| 10 | Projenin kendi `CustomBeforeMicrosoftCommonTargets` tanımı | Global özellik onu ezer — bilinen sınır, dokümana yazılır (OSYS'te böyle tanım yok; ortam değişkeni de yok). Geri düşüş mekanizması YOK (YAGNI); hata görülürse ayrı iş. |
| 11 | Cycles koşusunda öncelik (Faz 4) | **Kullanıcı onayı bekler.** Önerilen varsayılan: Cycles koşusu Balanced/Light'ta da Normal öncelik + tavansız koşar, işçi sayısı profilden; Ayarlar'da kapatılabilir ("Resolve cycles at full priority", varsayılan açık). Onay gelmeden Faz 4 başlamaz. |
| 12 | Derleyici sunucusu, slot sayısı, in-domain markup, portable PDB | Yapılmaz (ölçüldü: değmiyor — rapor §6). |

## 3. Review Focus (spec'in ima ettiği ama hiçbir task'ın doğrudan sınamadığı durumlar; ilgili task'a test eklendi)

1. **Üyenin çıktısı silinmiş, kaynağı aynı** → üye gerekli sayılmalı (K1). → Task 3.3 test (iv).
2. **Kardeş Visual Studio'da yeniden derlenmiş** (zaman kipi) → okuyucu gerekli (K2). → Task 3.3 test (v) ve
   Task 3.4 "kayıtlı yüzey ≠ diskteki yüzey" testi.
3. **Grup bir önceki koşuda Stop ile kesilmiş** → hiçbir kaydı güvenilir değil → herkes derlenir (K4). →
   Task 3.4 mevcut `stopped_group_invalidates_every_member` + yeni alanların yazılmadığını pinleyen test.
4. **`decimal` varsayılanı değişen kardeş** → okuyucu bayat sayılmalı (Ö1a). → Task 1.1.
5. **SDK-style WPF projesi** (`ProduceReferenceAssembly=true`) → targets ile derleme HATA VERMEMELİ (W1). →
   Task 2.3 acceptance testi (eski-stil + SDK-style mini proje).

## 4. Dosya haritası

| Dosya | Sorumluluk / değişiklik |
|---|---|
| `src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs` | Faz 1: parametre/dönüş/generic öznitelikleri, değer tipi alanları, layout |
| `tests/BuildOrchestrator.Tests/Incremental/ApiSurfaceHashTests.cs` | Faz 1 testleri (mevcut `Assembly(...)`/`HashOf` yardımcıları yeniden kullanılır) |
| `src/BuildOrchestrator.Core/MsBuild/WpfTemporaryAssemblyTargets.cs` (yeni) | Faz 2: targets + friend dosyası içeriği (tek kaynak), `EnsureWritten(cacheRoot)` |
| `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs`, `MsBuildInvoker.cs` (`MsBuildInvokeRequest`) | Faz 2: `-p:CustomBeforeMicrosoftCommonTargets=` argümanı |
| `src/BuildOrchestrator.Supervisor/Program.cs`, `RunCoordinator.cs` (`MsBuildToolset`, `InvokeOnceAsync`, `CommandLines`) | Faz 2: yolun çözümü ve isteğe taşınması |
| `tests/BuildOrchestrator.Tests/MsBuild/MsBuildArgumentsTests.cs`, yeni `WpfTemporaryAssemblyTargetsTests.cs`, yeni `Integration/WpfTemporaryAssemblyAcceptanceTests.cs` + `tests/Fixtures/WpfMini/**` | Faz 2 testleri |
| `src/BuildOrchestrator.Core/Incremental/IncrementalPlanner.cs`, `IncrementalRunBinder.cs`; `Supervisor/RunCoordinator.cs` (`IncrementalPlan`) | Faz 3: üye terimi (`MemberTermById`) |
| `src/BuildOrchestrator.Contracts/Model/ProjectModels.cs` (`BuildState`, yeni `CycleReadSurface`) | Faz 3: defter alanları |
| `src/BuildOrchestrator.Core/Planning/CycleMemberNeed.cs` (yeni), `Core/MsBuild/EngineFingerprint.cs` (yeni) | Faz 3: saf karar + parmak izi |
| `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`BuildCycleGroupAsync`, `CycleMemberState`, `PersistBuildStateOnSuccess`, yeni `ReportCarriedCycleMember`, `RecordCycleOutcome`) | Faz 3: tur 1 seçimi, atlanan üye raporu/persist |
| `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` (`CycleCompletedEvent.CompiledCount`), `App/ViewModels/StreamText.cs` | Faz 3: olay akışı metni |
| `tests/.../Planning/CycleMemberNeedTests.cs` (yeni), `Supervisor/CycleRoundsTests.cs`, `Incremental/IncrementalPlannerTests.cs`, `App/EventStreamTests.cs` | Faz 3 testleri |
| `ARCHITECTURE.md` §7.3, §7.5, §8.1, §8.4, §8.8, §9.2, §16, §17.5, §20, §22; `README.md` | Her fazda ilgili bölüm |

---

## Faz 1 — Yüzey özetinin kör noktaları (`perf/surface-hash-completeness`)

Ölçülmüş gerçek (rapor Ö1a, `blind/`): `decimal` varsayılan parametre değeri, `params`, `[CallerMemberName]`
ve struct private alanının tipi değişince `ApiSurfaceHash` AYNI kalıyor. Bugünkü etkisi: böyle bir değişiklikten
önce derlenen okuyucu tur 2'ye alınmaz. Faz 3 bu kanıtı koşular arasına taşıyacağı için bu faz ön koşuldur.

### Task 1.1: Parametre, dönüş değeri ve generic parametre öznitelikleri özete girer

**Files:**
- Modify: `src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs:212-262` (`RenderMethod`, `AppendGenericParameters`;
  `AppendAttributes` :276 yeniden kullanılır — kopya yazılmaz)
- Test: `tests/BuildOrchestrator.Tests/Incremental/ApiSurfaceHashTests.cs`

**Interfaces:** `ApiSurfaceHash.OfStream/OfFile` imzaları değişmez; yalnız özetin kapsamı genişler.

- [ ] **Adım 1 — kırmızı testler.** Mevcut `Assembly(...)` ve `Method(...)` yardımcılarının yanına parametre
  üreten bir yardımcı ekle ve üç test yaz:

```csharp
private static void MethodWithParameter(TypeBuilder type, string name, Type parameterType,
    Action<ParameterBuilder>? decorate = null)
{
    var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), [parameterType]);
    var parameter = method.DefineParameter(1, ParameterAttributes.None, "value");
    decorate?.Invoke(parameter);
    method.GetILGenerator().Emit(OpCodes.Ret);
}

private static CustomAttributeBuilder Attribute<T>(params object[] args) where T : Attribute =>
    new(typeof(T).GetConstructor(args.Select(a => a.GetType()).ToArray())!, args);

[Fact]
public void a_params_modifier_changes_the_hash()
{
    string? plain = HashOf(Assembly(t => MethodWithParameter(t, "Sum", typeof(int[]))));
    string? withParams = HashOf(Assembly(t => MethodWithParameter(t, "Sum", typeof(int[]),
        p => p.SetCustomAttribute(Attribute<ParamArrayAttribute>()))));
    Assert.NotEqual(plain, withParams); // params → ParamArrayAttribute: çağrı biçimi değişir
}

[Fact]
public void a_decimal_default_value_changes_the_hash()
{
    // decimal varsayılanı Constant tablosunda DEĞİL, parametredeki DecimalConstantAttribute'tadır.
    CustomAttributeBuilder Decimal(uint low) => Attribute<System.Runtime.CompilerServices.DecimalConstantAttribute>(
        (byte)2, (byte)0, (uint)0, (uint)0, low);
    string? eighteen = HashOf(Assembly(t => MethodWithParameter(t, "Rate", typeof(decimal), p => p.SetCustomAttribute(Decimal(18)))));
    string? twenty = HashOf(Assembly(t => MethodWithParameter(t, "Rate", typeof(decimal), p => p.SetCustomAttribute(Decimal(20)))));
    Assert.NotEqual(eighteen, twenty);
}

[Fact]
public void a_caller_info_attribute_changes_the_hash()
{
    string? plain = HashOf(Assembly(t => MethodWithParameter(t, "Who", typeof(string))));
    string? caller = HashOf(Assembly(t => MethodWithParameter(t, "Who", typeof(string),
        p => p.SetCustomAttribute(Attribute<System.Runtime.CompilerServices.CallerMemberNameAttribute>()))));
    Assert.NotEqual(plain, caller);
}
```

  Generic parametre için: `type.DefineGenericParameters("T")[0].SetCustomAttribute(...)` ile bir test daha
  (`a_generic_parameter_attribute_changes_the_hash`); dönüş değeri için `method.DefineParameter(0, ...)` ile
  `a_return_value_attribute_changes_the_hash`.

- [ ] **Adım 2 — kırmızıyı gör.** `dotnet test ... --filter "FullyQualifiedName~ApiSurfaceHashTests"` → beş yeni
  test FAIL (özetler eşit).

- [ ] **Adım 3 — en küçük uygulama.** `RenderMethod`'da parametre döngüsüne özniteliklerin ayrı satırlarını ekle
  (satır tek kalır, öznitelikler metot satırından sonra gelir — `AppendAttributes` zaten sıralı yazar):

```csharp
// RenderMethod içinde, parametre döngüsünün yerine:
var parameterAttributes = new StringBuilder();
foreach (var handle in method.GetParameters())
{
    var parameter = reader.GetParameter(handle);
    line.Append(" p").Append(parameter.SequenceNumber).Append('=')
        .Append(reader.GetString(parameter.Name))
        .Append('/').Append((int)parameter.Attributes)
        .Append(RenderConstant(reader, parameter.GetDefaultValue()));
    // Parametre öznitelikleri yüzeydir: params, decimal/DateTime varsayılanı, Caller*, Dynamic, tuple adları...
    AppendAttributes(reader, parameter.GetCustomAttributes(), parameterAttributes,
        indent: "  p" + parameter.SequenceNumber.ToString(CultureInfo.InvariantCulture) + " ");
}
AppendGenericParameters(reader, method.GetGenericParameters(), provider, line);
line.Append('\n');
line.Append(parameterAttributes);
AppendAttributes(reader, method.GetCustomAttributes(), line, indent: "  ");
return line.ToString();
```

  `AppendGenericParameters`'ta her generic parametre için
  `AppendAttributes(reader, parameter.GetCustomAttributes(), <ayrı builder>, indent: " gp" + index + " ")` ekle
  ve metot/tip satırından sonra yaz (tip için `RenderType`'ta aynı desen).

- [ ] **Adım 4 — yeşil.** Aynı filtreyle tüm `ApiSurfaceHashTests` PASS (eskiler dahil:
  `same_declarations_with_different_bodies_hash_identically` hâlâ geçer — gövde parametre özniteliği taşımaz).
- [ ] **Adım 5 — commit.** `git commit -m "fix(core): yuzey ozeti parametre, donus ve generic parametre ozniteliklerini kapsar"`

### Task 1.2: Değer tiplerinin private alanları ve tip layout'u özete girer

**Files:** `ApiSurfaceHash.cs:147-162` (alan döngüsü), `:129-135` (`RenderType`), test dosyası aynı.

- [ ] **Adım 1 — kırmızı testler.**

```csharp
private static byte[] StructAssembly(Type privateFieldType)
{
    var name = new AssemblyName("Surface.Probe");
    var builder = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
    var module = builder.DefineDynamicModule("M");
    var type = module.DefineType("N.Money", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType));
    type.DefineField("Amount", typeof(int), FieldAttributes.Public);
    type.DefineField("_pad", privateFieldType, FieldAttributes.Private);
    type.CreateType();
    using var stream = new MemoryStream();
    builder.Save(stream);
    return stream.ToArray();
}

[Fact]
public void a_private_field_of_a_struct_changes_the_hash()
{
    // Kesin atama ve `unmanaged` kuralı struct'ın BÜTÜN alanlarına bakar — Roslyn reference assembly'leri de bu
    // yüzden struct alanlarını atmaz.
    Assert.NotEqual(HashOf(StructAssembly(typeof(int))), HashOf(StructAssembly(typeof(object))));
}

[Fact]
public void a_private_field_of_a_class_still_does_not_change_the_hash()
{
    string? a = HashOf(Assembly(t => t.DefineField("_pad", typeof(int), FieldAttributes.Private)));
    string? b = HashOf(Assembly(t => t.DefineField("_pad", typeof(object), FieldAttributes.Private)));
    Assert.Equal(a, b);
}

[Fact]
public void an_explicit_struct_layout_size_changes_the_hash()
{
    // StructLayout(Size=…) ClassLayout tablosuna yazılır; unsafe tüketicide sizeof değişir.
    static byte[] Sized(int size) { /* StructAssembly gibi, type.DefineType(... , typeof(ValueType), packingSize: PackingSize.Unspecified, typesize: size) */ }
    Assert.NotEqual(HashOf(Sized(8)), HashOf(Sized(16)));
}
```

- [ ] **Adım 2 — kırmızıyı gör** (üçüncü test için `DefineType` overload'ı `typesize` alır; `Sized` gövdesini
  `StructAssembly`'nin kopyası olmadan, ortak bir `ValueTypeAssembly(Action<TypeBuilder>, int size = 0)`
  yardımcısıyla kur — kopya yasağı).
- [ ] **Adım 3 — uygulama.** Alan döngüsünde:

```csharp
var access = field.Attributes & FieldAttributes.FieldAccessMask;
// Değer tipinde private alan da yüzeydir (kesin atama, unmanaged, layout); sınıfta değildir.
if (access == FieldAttributes.Private && !IsValueType(reader, type)) continue;
```

  ve `RenderType`'ta `impl=` sonrasına:

```csharp
var layout = type.GetLayout();
if (!layout.IsDefault) text.Append(" layout=").Append(layout.Size).Append('/').Append(layout.PackingSize);
```

  `IsValueType`: `type.BaseType` bir `TypeReference`/`TypeDefinition` ise tam adı `System.ValueType` ya da
  `System.Enum` olan tip (mevcut `RenderTypeHandle` ile ad çözülür; `[mscorlib]`/`[System.Runtime]` öneki
  `EndsWith("System.ValueType")` ile tolere edilir).

- [ ] **Adım 4 — yeşil;** `IncrementalPlannerTests`, `CycleRoundsTests` de yeşil (özet tüketicileri).
- [ ] **Adım 5 — doküman.** ARCHITECTURE §8.8'deki `ApiSurfaceHash` parantezi ("no IL, no MVID…") yüzeyin ne
  içerdiğini anlatır biçimde yeniden yazılır: "declarations, their attributes including parameter, return and
  generic-parameter attributes, every field of a value type, explicit layout; no IL, no MVID…". Eski iddia
  changelog gibi anılmaz.
- [ ] **Adım 6 — commit.** `git commit -m "fix(core): deger tipi alanlari ve layout yuzey ozetine girer"`
- [ ] **Faz 1 kapanışı:** tam süit (`Category!=Acceptance`) yeşil → `develop`'a `--no-ff` merge
  (`merge: Yuzey ozeti kor noktalari kapatildi (perf/surface-hash-completeness)`) → push → branch sil.

---

## Faz 2 — WPF geçici assembly'si metadata-only (`perf/wpf-temporary-assembly`)

Ölçülmüş gerçek (rapor B2, Ek): `_wpftmp` projesini `ProduceOnlyReferenceAssembly=true` ile derlemek tek üye
senaryosunda 63 → 48 sn, branch senaryosunda 132 → 109 sn, Rebuild'de 84 → 75 sn; BAML/DLL boyutu/yüzey 17/17
üyede ve 187 projenin tamamında aynı. İki tuzak deneyle doğrulandı ve kapatıldı: SDK-style projede
`/refout`+`/refonly` çakışması (CS8308) → `ProduceReferenceAssembly=false`; internal üyeye bağlanan XAML
(MC3072) → geçici assembly'ye `InternalsVisibleTo`.

### Task 2.1: Targets içeriği tek kaynakta; motor açılışta önbellek köküne yazar

**Files:**
- Create: `src/BuildOrchestrator.Core/MsBuild/WpfTemporaryAssemblyTargets.cs`
- Test: `tests/BuildOrchestrator.Tests/MsBuild/WpfTemporaryAssemblyTargetsTests.cs` (yeni)

**Interfaces (Produces):**
```csharp
namespace BuildOrchestrator.Core.MsBuild;

/// WPF'in yerel tipli XAML için derlediği geçici assembly ("<proje>_<rastgele>_wpftmp") yalnız yansımayla okunur;
/// gövdesiz (metadata-only) derlenmesi çıktıyı değiştirmez, süreyi kısaltır. Üç öğe birlikte gerekir (bkz. §9.2).
public static class WpfTemporaryAssemblyTargets
{
    public const string TargetsFileName = "wpf-temporary-assembly.targets";
    public const string FriendFileName = "wpf-temporary-assembly-friend.cs";
    public const string FriendAssemblyName = "BuildOrchestrator.WpfTemporaryAssemblyFriend";
    /// Targets dosyasının içeriği (XML). Friend dosyasının yolu $(MSBuildThisFileDirectory) ile göreli.
    public static string TargetsContent { get; }
    public static string FriendContent { get; }
    /// <cacheRoot>\msbuild\ altına iki dosyayı yazar (içerik aynıysa dokunmaz), targets'ın tam yolunu döner.
    public static string EnsureWritten(string cacheRoot);
}
```

- [ ] **Adım 1 — kırmızı testler** (geçici klasörde):

```csharp
[Fact]
public void writes_both_files_under_msbuild_and_returns_the_targets_path()
{
    using var dir = new TempDir(); // mevcut test yardımcısı neyse o (tests'te TempDir/Directory.CreateTempSubdirectory kullanımı aranır; kopya üretilmez)
    string path = WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
    Assert.Equal(Path.Combine(dir.Path, "msbuild", WpfTemporaryAssemblyTargets.TargetsFileName), path);
    Assert.Equal(WpfTemporaryAssemblyTargets.TargetsContent, File.ReadAllText(path));
    Assert.Equal(WpfTemporaryAssemblyTargets.FriendContent, File.ReadAllText(Path.Combine(dir.Path, "msbuild", WpfTemporaryAssemblyTargets.FriendFileName)));
}

[Fact]
public void the_targets_only_act_on_wpftmp_projects_and_carry_the_three_elements()
{
    string t = WpfTemporaryAssemblyTargets.TargetsContent;
    Assert.Contains("$(MSBuildProjectName.EndsWith('_wpftmp'))", t);
    Assert.Contains("<ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>", t);
    Assert.Contains("<ProduceReferenceAssembly>false</ProduceReferenceAssembly>", t);
    Assert.Contains("<Compile Include=\"$(MSBuildThisFileDirectory)" + WpfTemporaryAssemblyTargets.FriendFileName + "\"", t);
    Assert.Contains("InternalsVisibleTo(\"" + WpfTemporaryAssemblyTargets.FriendAssemblyName + "\")", WpfTemporaryAssemblyTargets.FriendContent);
    // Varsayılan Custom.Before dosyası zincirde kalır (W6)
    Assert.Contains("Custom.Before.Microsoft.Common.targets", t);
}

[Fact]
public void rewriting_is_idempotent_and_does_not_touch_an_identical_file()
{
    using var dir = new TempDir();
    string path = WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
    var stamp = File.GetLastWriteTimeUtc(path);
    WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
    Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    File.WriteAllText(path, "<Project />");
    WpfTemporaryAssemblyTargets.EnsureWritten(dir.Path);
    Assert.Equal(WpfTemporaryAssemblyTargets.TargetsContent, File.ReadAllText(path)); // bozulan içerik onarılır
}
```

- [ ] **Adım 2 — kırmızıyı gör** (sınıf yok → derleme hatası sayılır; `dotnet test` filtresi ile).
- [ ] **Adım 3 — uygulama.** İçerik, ölçümde kullanılan dosyanın birebir karşılığıdır
  (`.claude/temp/cycle-resolve-perf-2026-10-02/mini/bo-wpftmp-v3.targets`):

```xml
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <!-- Build Orchestrator: WPF's temporary local-type assembly is only reflected over; compile it as metadata only. -->
  <Import Project="$(MSBuildExtensionsPath)\v$(MSBuildToolsVersion)\Custom.Before.Microsoft.Common.targets"
          Condition="Exists('$(MSBuildExtensionsPath)\v$(MSBuildToolsVersion)\Custom.Before.Microsoft.Common.targets')" />
  <PropertyGroup Condition="$(MSBuildProjectName.EndsWith('_wpftmp'))">
    <ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>
    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>
  </PropertyGroup>
  <ItemGroup Condition="$(MSBuildProjectName.EndsWith('_wpftmp'))">
    <Compile Include="$(MSBuildThisFileDirectory)wpf-temporary-assembly-friend.cs" />
  </ItemGroup>
</Project>
```

  Friend dosyası: `[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("BuildOrchestrator.WpfTemporaryAssemblyFriend")]`
  + bir satır yorum ("compiled only into WPF's temporary assembly so that a metadata-only build keeps internal
  members"). `EnsureWritten`: klasörü kur, mevcut içerik `string.Equals` ile aynıysa yazma, aksi hâlde yaz
  (atomik yazım gerekmez; motor tek yazıcı).
- [ ] **Adım 4 — yeşil.** Commit: `git commit -m "feat(core): WPF gecici assembly targets'i tek kaynakta ve onbellek kokune yazilir"`

### Task 2.2: Argüman sözleşmesine `-p:CustomBeforeMicrosoftCommonTargets=` eklenir

**Files:**
- Modify: `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:22-28, 52-58`; `MsBuildInvoker.cs:11-13`
  (`MsBuildInvokeRequest` + `CustomBeforeTargets` alanı); `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:68`
  (`MsBuildToolset` + `CustomBeforeTargetsPath`), `:2036-2051` (`InvokeOnceAsync` istek kurulumu), `:2136-2143`
  (`CommandLines`), `Program.cs:223-230` (`ResolveMsBuildAsync` → `WpfTemporaryAssemblyTargets.EnsureWritten(cacheRoot)`).
- Test: `tests/BuildOrchestrator.Tests/MsBuild/MsBuildArgumentsTests.cs` (pinlenen liste YENİ kuralı pinler),
  `Supervisor/RunCoordinatorTests.cs` (komut satırı ilk log satırı testi varsa güncellenir).

**Interfaces (Produces):**
```csharp
public static IReadOnlyList<string> Build(string projectPath, string configuration,
    MsBuildTarget target = MsBuildTarget.Build, string? customBeforeTargets = null);
// customBeforeTargets null değilse liste sonuna "-p:CustomBeforeMicrosoftCommonTargets=<yol>" eklenir; yol tırnaklanmaz
// (ArgumentList üzerinden geçer, WindowsCommandLine.Build kaçışlar).
public sealed record MsBuildInvokeRequest(string ProjectId, string Configuration, string SolutionDir, bool NeedsRestore,
    MsBuildTarget Target = MsBuildTarget.Build, string? CustomBeforeTargets = null);
public sealed record MsBuildToolset(IMsBuildInvoker Invoker, string MsBuildExePath, string? CustomBeforeTargetsPath = null);
```

- [ ] **Adım 1 — kırmızı testler** (`MsBuildArgumentsTests`):

```csharp
[Fact]
public void the_build_list_carries_the_wpf_temporary_assembly_targets_when_given()
{
    var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", customBeforeTargets: @"C:\state\msbuild\wpf-temporary-assembly.targets");
    Assert.Contains(@"-p:CustomBeforeMicrosoftCommonTargets=C:\state\msbuild\wpf-temporary-assembly.targets", args);
    Assert.Equal(1, args.Count(a => a.StartsWith("-p:CustomBeforeMicrosoftCommonTargets=", StringComparison.Ordinal)));
}

[Fact]
public void without_a_targets_path_the_list_is_unchanged() // yalıtılmış test motoru ve eski çağıranlar için
    => Assert.DoesNotContain(MsBuildArguments.Build(@"c:\r\p.csproj", "Debug"), a => a.Contains("CustomBeforeMicrosoftCommonTargets"));

[Fact]
public void restore_never_carries_the_targets()
    => Assert.DoesNotContain(MsBuildArguments.RestorePackagesConfig(@"c:\r\p.csproj", @"c:\r\"), a => a.Contains("CustomBeforeMicrosoftCommonTargets"));
```

  `PlanFor(request)` testi: istekte `CustomBeforeTargets` doluysa Build listesi taşır, Restore listesi taşımaz.
  Mevcut "liste tam olarak şudur" testleri varsa YENİ sözleşmeyi pinleyecek şekilde güncellenir; doc yorumuna
  eski iddia + "ölçüm: WPF geçici assembly −%18…−24" yazılır.

- [ ] **Adım 2 — kırmızıyı gör.**
- [ ] **Adım 3 — uygulama.** `Build(...)` listeyi kurar, `customBeforeTargets is not null` ise ekler;
  `PlanFor` → `Build(request.ProjectId, request.Configuration, request.Target, request.CustomBeforeTargets)`;
  `InvokeOnceAsync` isteğe `CustomBeforeTargets: run.CustomBeforeTargetsPath` yazar (RunContext'e toolset'ten
  gelen yol eklenir; `MsBuildToolset.CustomBeforeTargetsPath`); `CommandLines` aynı parametreyi geçer (ilk log
  satırı gerçek komut satırıdır — otomatik olarak yeni argümanı gösterir); `Program.ResolveMsBuildAsync`
  toolset'i kurarken `WpfTemporaryAssemblyTargets.EnsureWritten(cacheRoot)` sonucunu verir (`cacheRoot`
  `Main`'de zaten var; `ResolveMsBuildAsync` imzasına parametre olarak taşınır).
- [ ] **Adım 4 — yeşil;** `RunCoordinatorTests` ve `CycleRoundsTests` içinde komut satırını pinleyen testler
  (varsa) yeni argümanla güncellenir.
- [ ] **Adım 5 — commit.** `git commit -m "feat(engine): WPF gecici assembly'si metadata-only derlenir (CustomBeforeMicrosoftCommonTargets)"`

### Task 2.3: Acceptance testi — gerçek MSBuild ile iki mini WPF projesi, çıktı eşdeğerliği

**Files:**
- Create: `tests/BuildOrchestrator.Tests/Fixtures/WpfMini/Old/{Mini.csproj, MyControl.cs, View.xaml.cs, ViewPublic.xaml, ViewInternal.xaml}`,
  `tests/BuildOrchestrator.Tests/Fixtures/WpfMini/Sdk/{MiniSdk.csproj, MyControl.cs, View.xaml, View.xaml.cs}`,
  `tests/BuildOrchestrator.Tests/Fixtures/WpfMini/Directory.Build.props` (`<Project />` — repo props'unun sızmasını keser)
  — içerikler `.claude/temp/cycle-resolve-perf-2026-10-02/mini/` altındaki deney dosyalarının birebir kopyası;
  csproj'lar `tests` projesinin derlemesine GİRMEZ (`<Compile Remove="Fixtures/**" />` ve `<None Include=... CopyToOutputDirectory=PreserveNewest>`).
- Create: `tests/BuildOrchestrator.Tests/Integration/WpfTemporaryAssemblyAcceptanceTests.cs`
  (`[Trait("Category","Acceptance")]`, `[SkippableFact]` + `Skip.IfNot(MsBuild çözülebiliyor)`).

- [ ] **Adım 1 — test.** Her mini proje için: fixture'ı geçici klasöre kopyala; aynı `MsBuildArguments.Build`
  listesiyle iki kez derle — targets'sız ve `WpfTemporaryAssemblyTargets.EnsureWritten(tempRoot)` ile —
  `obj\**\*.baml` içerik özetleri ve çıktı DLL boyutu eşit, çıktı DLL'de `ReferenceAssemblyAttribute` ve
  `WpfTemporaryAssemblyFriend` metni YOK, exit 0. Eski-stil projede `ViewInternal.xaml` (internal özellik)
  dalı da geçmeli (MC3072 çıkmaz). MSBuild yolu `MsBuildResolver` ile çözülür; çözülemezse `Skip`.
- [ ] **Adım 2 — koştur:** `dotnet test ... --filter "FullyQualifiedName~WpfTemporaryAssemblyAcceptanceTests"`
  → PASS (bu makinede VS 18.9). Kontrol: targets'ta `ProduceReferenceAssembly=false` satırını geçici olarak
  silince SDK-style dal CS8308 ile FAIL etmeli (kırmızı kanıtı), geri koy.
- [ ] **Adım 3 — doküman.** ARCHITECTURE §17.5 Acceptance listesine test eklenir (ne zaman koşar, ne ister).
- [ ] **Adım 4 — commit.** `git commit -m "test(integration): WPF gecici assembly metadata-only ciktisinin esdegerligi gercek MSBuild ile pinlendi"`

### Task 2.4: Doküman ve kapanış

- [ ] ARCHITECTURE §9.2 argüman sözleşmesi: yeni satır
  `-p:CustomBeforeMicrosoftCommonTargets=<cache>\msbuild\wpf-temporary-assembly.targets` + madde: ne yaptığı (üç
  öğe ve nedenleri), neyi DEĞİŞTİRMEDİĞİ (OutDir, obj düzeni, nihai çıktı — ölçüm: BAML/DLL eşdeğer), bilinen
  sınır (projenin kendi `CustomBeforeMicrosoftCommonTargets` tanımını ezer; varsayılan Custom.Before dosyası
  zincirde kalır), doğrulanan toolset'ler (VS 18.9, VS 2022 eski-stil). Rakam yerine "roughly a fifth of a
  WPF project's compile" gibi dayanıklı dil.
- [ ] §16 Diske yazılanlar: `msbuild\` klasörü ve iki dosya (motor her açılışta onarır). §22 kod haritası:
  `WpfTemporaryAssemblyTargets.cs`. README "Builds are shelled out…" maddesine tek cümle.
- [ ] Tam süit yeşil + acceptance testi bir kez → `develop`'a `--no-ff` merge
  (`merge: WPF gecici assembly metadata-only derlenir (perf/wpf-temporary-assembly)`) → push → branch sil.
- [ ] **Ölçüm (kapanış kanıtı):** §5'teki harness ile tek üye senaryosu (S1) taban ~63 sn → hedef ≤ 50 sn
  (ölçülen 48,2). Uygulama kapalı, `snap.py save` önce, `restore`+`verify` sonra.

---

## Faz 3 — Üye düzeyi artımlı tur 1 (`perf/resolve-member-skip`)

Ölçülmüş gerçek (rapor B1): tek üyede tek satır değişince 17 üyenin 17'si tam derleniyor (63 sn); yalnız o üye
6,3 sn. Git geçmişinde UI grubunu kirleten hareketlerin %40'ı tek üye. Branch değişimi senaryosunda kazanç yok
(herkes gerekli) — bu doğru davranıştır, plan onu korur.

### Task 3.1: Üye terimi planlamadan dışarı çıkar (`MemberTermById`)

**Files:**
- Modify: `src/BuildOrchestrator.Core/Incremental/IncrementalPlanner.cs:185-211` (`ComputeComponent`),
  dönüş tipi; `IncrementalRunBinder.cs:122` (`Bind` dönüşü); `Supervisor/RunCoordinator.cs:53-60` (`IncrementalPlan`),
  `Program.cs:172-207` (`ComputeIncremental` → plan).
- Test: `tests/BuildOrchestrator.Tests/Incremental/IncrementalPlannerTests.cs`

**Interfaces (Produces):**
```csharp
// IncrementalPlanner: bileşik imzanın yanında üye başına terim. Yalnız SCC üyeleri için dolu.
public sealed record IncrementalSignatures(IReadOnlyDictionary<string, string> SignatureById,
                                           IReadOnlyDictionary<string, string> MemberTermById);
// IncrementalPlan'a yeni alan: IReadOnlyDictionary<string, string>? MemberTermById = null
```

- [ ] **Adım 1 — kırmızı testler** (mevcut planner fixture'ı ile iki üyeli SCC):
  (a) `member_term_ignores_sibling_content`: A↔B döngüsünde B'nin içeriği değişince A'nın terimi AYNI, B'nin
  terimi ve bileşik imza FARKLI; (b) `member_term_follows_outside_upstream`: A'nın grup dışı upstream'i U
  değişince A'nın terimi değişir (Safe); (c) `member_term_equals_the_composite_input`: terim,
  `BuildSignature.Compute(member, cfg, content, dep => intra ? NullMarker : Upstream(dep))` ile birebir aynı
  (kopya değil, aynı çağrı — `ComputeComponent` içinden toplanır).
- [ ] **Adım 2 — kırmızıyı gör.**
- [ ] **Adım 3 — uygulama.** `ComputeComponent` döngüsünde `BuildSignature.Compute(...)` sonucu `sb`'ye
  eklenmeden önce `memberTerm[id] = term;` (sözlük `computedMemo` gibi dışarıda). Planner'ın dönüşüne
  `MemberTermById` eklenir; `Bind` ve `IncrementalPlan` taşır.
- [ ] **Adım 4 — yeşil. Commit:** `git commit -m "feat(core): SCC uyelerinin kendi terimi plana tasinir (MemberTermById)"`

### Task 3.2: Defter alanları ve motor parmak izi

**Files:**
- Modify: `src/BuildOrchestrator.Contracts/Model/ProjectModels.cs:136-200` (`BuildState` + 3 alan + `Equals`),
  yeni record `CycleReadSurface`.
- Create: `src/BuildOrchestrator.Core/MsBuild/EngineFingerprint.cs`.
- Test: `tests/BuildOrchestrator.Tests/State/BuildStateStoreTests.cs` (round-trip), yeni `MsBuild/EngineFingerprintTests.cs`.

**Interfaces (Produces):**
```csharp
/// Bir döngü üyesinin son GÜVENİLİR derlemesinde okuduğu kardeş yüzeyi: üretici id'si, okunan dosya, yüzey özeti.
public sealed record CycleReadSurface(string Producer, string File, string Hash);

// BuildState'e SONA ve default'lu eklenir (eski kayıtlar null okur → kural (iii): üye gerekli):
//   string? CycleMemberTerm = null,
//   IReadOnlyList<CycleReadSurface>? CycleReadSurfaces = null,
//   string? CycleEngineFingerprint = null
// Equals: listeler içerikle karşılaştırılır (DepIssueRoots deseni).

public static class EngineFingerprint
{
    /// SHA-256(msbuildExePath + dosya sürümü + MsBuildArguments.Build("<p>", "<c>").Skip(1) birleşimi).
    /// Toolset ya da argüman sözleşmesi değişince farklı okunur; proje ve configuration değeri girmez.
    public static string Compute(string msbuildExePath, Func<string, string, IReadOnlyList<string>> build);
}
```

- [ ] **Adım 1 — kırmızı testler:** (a) `BuildState` JSON round-trip yeni alanları korur, eski JSON (alansız)
  null okur; (b) `Equals` listeyi içerikle karşılaştırır; (c) `EngineFingerprint.Compute` aynı girdide
  deterministik, argüman listesi değişince farklı, proje yolu değişince AYNI.
- [ ] **Adım 2 — kırmızıyı gör. Adım 3 — uygulama. Adım 4 — yeşil.**
- [ ] **Adım 5 — commit.** `git commit -m "feat(contracts,core): donguler icin defter alanlari (uye terimi, okunan yuzeyler, motor parmak izi)"`

### Task 3.3: Saf karar — `CycleMemberNeed.Decide` (Core, I/O yok)

**Files:**
- Create: `src/BuildOrchestrator.Core/Planning/CycleMemberNeed.cs`
- Test: `tests/BuildOrchestrator.Tests/Planning/CycleMemberNeedTests.cs` (yeni)

**Interfaces (Produces):**
```csharp
namespace BuildOrchestrator.Core.Planning;

/// Tur 1'de hangi üyelerin derleneceği. Saf: I/O, process, log YOK [D3].
public static class CycleMemberNeed
{
    public sealed record MemberEvidence(
        BuildState? Record,                    // defterdeki kayıt (LedgerAtStart)
        string? CurrentTerm,                   // IncrementalPlan.MemberTermById
        OutputCheck? Output);                  // IncrementalPlan.ChecksById (null ⇒ kanıt yok)

    public sealed record Decision(
        IReadOnlyList<string> ToBuild,         // build-order sıralı alt liste
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> CarriedReadStates,
        IReadOnlyDictionary<string, string> Reasons); // üye → neden gerekli (decision.log için; atlananlar yok)

    /// surfaceState: üretici → dosya → yüzey özeti (grup başında diskten okunan). engineFingerprint: bu koşunun.
    public static Decision Decide(IReadOnlyList<string> members, Func<string, MemberEvidence> evidence,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> surfaceState, string engineFingerprint);
}
```

  Kural (karar 2): üye gerekli ⇔ `Record is null` · `Record.LastResult != Succeeded` · `Record.CycleMemberTerm`
  null ya da `!= CurrentTerm` · `Record.CycleReadSurfaces` null · `Record.CycleEngineFingerprint != engineFingerprint`
  · `Output is null` · `Output.Mode != EvidenceMode.Ledger` · `Output.EvidenceMissing` · `!Output.FedIntact` ·
  kayıtlı her `(Producer, File, Hash)` için `surfaceState[Producer][File]` yok ya da `!= Hash`. Gerekli
  olmayan üyenin `CarriedReadStates[member] = kayıttan kurulan üretici→dosya→özet`. `Reasons` metinleri
  İngilizce ve kısa (`"own inputs changed"`, `"read surface moved: X"`, `"no trusted record"`, `"output evidence
  missing"`, `"output built outside this tool"`, `"engine changed"`).

- [ ] **Adım 1 — kırmızı testler** (her kural için bir test; fixture tek yerde: `Member(term, surfaces, output)`
  yardımcısı + `Ledger(...)` kurucu):

```csharp
[Fact] public void a_member_whose_term_is_unchanged_and_surfaces_match_is_carried() { /* ToBuild boş, CarriedReadStates[A] kayıttaki iki dosyayı taşır */ }
[Fact] public void a_changed_term_makes_the_member_needed() { /* Reasons[A] == "own inputs changed" */ }
[Fact] public void a_moved_read_surface_makes_the_reader_needed() { /* surfaceState[B][file] != kayıt → A gerekli */ }
[Fact] public void a_recorded_file_no_longer_known_makes_the_reader_needed()
[Fact] public void a_missing_or_untrusted_record_makes_the_member_needed() { /* Record null; LastResult Failed */ }
[Fact] public void a_record_without_cycle_fields_makes_the_member_needed() { /* eski defter */ }
[Fact] public void missing_output_evidence_makes_the_member_needed() { /* EvidenceMissing */ }
[Fact] public void an_output_built_outside_this_tool_makes_the_member_needed() { /* Mode == Time */ }
[Fact] public void broken_fed_copies_make_the_member_needed() { /* FedIntact false */ }
[Fact] public void a_different_engine_fingerprint_makes_every_member_needed()
[Fact] public void to_build_keeps_build_order()
```

- [ ] **Adım 2 — kırmızıyı gör. Adım 3 — uygulama** (tek `foreach`, erken `continue`'lu kural zinciri;
  `Reasons` ilk eşleşen nedeni yazar). **Adım 4 — yeşil.**
- [ ] **Adım 5 — commit.** `git commit -m "feat(core): tur 1 icin uye gereklilik karari (CycleMemberNeed)"`

### Task 3.4: Tur döngüsü yalnız gereken üyeleri derler; atlananlar raporlanır ve defteri yenilenir

**Files:**
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs`: `BuildCycleGroupAsync` (:1461-1799 —
  `state[id]` ilk durumu, `toBuild` seçimi, `staleNow`), `CycleMemberState` (:1962-1982 — `Carried` bayrağı),
  `PersistBuildStateOnSuccess` (:2159 — `CycleMemberRecord` parametresi), yeni `ReportCarriedCycleMember`,
  `RecordCycleOutcome` (:1818 — `compiledCount`), `RunContext` (parmak izi + `LedgerAtStart` zaten var).
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs:516-517` (`CycleCompletedEvent` +
  `int CompiledCount = -1` — SONA, default'lu; -1 = eski motor), `App/ViewModels/StreamText.cs:214`
  (`CycleCompleted` metni: `… converged — 17 members, 1 compiled, 1 round …`; CompiledCount < 0 ise eski metin).
- Test: `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` (mevcut `RoundRecorder`/`FakeInvoker`
  fixture'ı; `apiSurface` seam'i), `App/EventStreamTests.cs`.

**Interfaces (Consumes):** Task 3.1 `IncrementalPlan.MemberTermById`, Task 3.2 alanları ve `EngineFingerprint`,
Task 3.3 `CycleMemberNeed.Decide`. **Produces:** `CycleCompletedEvent.CompiledCount`.

- [ ] **Adım 1 — kırmızı testler** (`CycleRoundsTests`; her biri bir kuralı pinler):

```csharp
[Fact]
public async Task round_one_compiles_only_the_member_whose_own_inputs_changed()
{
    // A↔B döngüsü; önceki Cycles koşusu yakınsamış (defterde terim + okunan yüzeyler + parmak izi); şimdi
    // yalnız A'nın içeriği değişik. Beklenen: invoke edilen tek proje A; B için ProjectSkippedEvent("up to
    // date"); B'nin defter kaydı YENİ bileşik imzayı taşır, LastDurationMs eskisi kalır; CycleCompletedEvent
    // CompiledCount == 1, MemberCount == 2, Rounds == 1; decision.log'da "B: skipped — up to date (carried:
    // own inputs and read surfaces unchanged)" ve "cycle A: converged (2 members, 1 compiled)".
}

[Fact]
public async Task a_carried_member_whose_read_surface_moves_in_round_one_is_compiled_in_round_two()
{
    // A değişik ve yüzeyi oynuyor (apiSurface seam'i A'nın dosyası için derlemeden sonra farklı özet verir);
    // B taşınmış. Beklenen: tur 1 yalnız A; tur sonu B bayat (kayıtlı yüzey ≠ yeni) → tur 2 yalnız B; iki
    // tur sonunda Converged; B için ProjectSucceededEvent (skipped DEĞİL), süresi yalnız tur 2.
}

[Fact]
public async Task a_member_without_cycle_fields_in_its_record_is_compiled() // eski defter → bugünkü davranış
[Fact]
public async Task a_member_whose_output_evidence_is_missing_is_compiled_even_if_its_term_is_unchanged() // K1
[Fact]
public async Task a_member_whose_output_is_in_time_mode_is_compiled() // K2/K3 (Visual Studio / satır menüsü)
[Fact]
public async Task a_different_engine_fingerprint_compiles_every_member() // K9d
[Fact]
public async Task a_recorded_surface_that_differs_from_the_disk_at_group_start_compiles_the_reader() // K2 ikinci yüz
[Fact]
public async Task converged_group_writes_term_surfaces_and_fingerprint_for_compiled_and_carried_members()
[Fact]
public async Task non_converged_and_stopped_groups_write_no_cycle_fields() // mevcut iki test genişletilir (K4/K5)
[Fact]
public async Task without_surface_evidence_round_one_still_compiles_everyone() // hashMode=false → bugünkü davranış
```

  App: `EventStreamTests` — `CycleCompletedEvent(CompiledCount: 1)` satırı "… 17 members, 1 compiled …" okur;
  `CompiledCount: -1` eski metni korur.

- [ ] **Adım 2 — kırmızıyı gör** (`--filter "FullyQualifiedName~CycleRoundsTests"`).
- [ ] **Adım 3 — uygulama** (`BuildCycleGroupAsync`, mevcut yapı korunur):
  1. `hashMode` kurulup `surfaceState` dolduktan sonra:
     ```csharp
     var fingerprint = run.EngineFingerprint; // RunContext: PlanAndRunAsync'te EngineFingerprint.Compute(toolset.MsBuildExePath, (p, c) => MsBuildArguments.Build(p, c, customBeforeTargets: toolset.CustomBeforeTargetsPath))
     IReadOnlyList<string> toBuild = members;
     IReadOnlyDictionary<string, string> needReasons = EmptyReasons;
     if (hashMode && run.Incremental?.MemberTermById is { } terms)
     {
         var decision = CycleMemberNeed.Decide(members,
             id => new CycleMemberNeed.MemberEvidence(run.LedgerAtStart?.GetValueOrDefault(id), terms.GetValueOrDefault(id), run.Incremental.ChecksById?.GetValueOrDefault(id)),
             surfaceState, fingerprint);
         toBuild = decision.ToBuild; needReasons = decision.Reasons;
         foreach (var (id, carried) in decision.CarriedReadStates)
         {
             state[id].Result = BuildResult.Succeeded;            // güvenilir kayıt — bkz. karar 2
             state[id].Carried = true;
             state[id].ReadStates = carried.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
         }
     }
     ```
     `decision.log`: gerekli üyeler için `"{name}: round 1 — {reason}"`, atlananlar için grup sonunda.
  2. Tur döngüsü `toBuild` ile başlar (bugün `members`); `CompileOneAsync` içinde başarılı invoke sonrası
     `member.Carried = false` (artık derlendi). `failed`/`staleNow` hesapları `members` üzerinde AYNEN kalır
     (taşınan üyenin `Result` Succeeded, `ReadStates` kayıttan).
  3. `finally` içinde raporlama: `member.Carried && decision == Converged` ise
     `ReportCarriedCycleMember(run, id, member.DepIssues)` → `ReportSkipped(..., SkipReasons.UpToDate,
     cycleUnconverged: false, detail: "carried: own inputs and read surfaces unchanged")` + yeni
     `PersistBuildStateOnCarriedMember(run, id)` (mevcut kaydı yeni bileşik imza, `LastRunAt`, `BuiltCommit`,
     `LastBranch`, `DepIssue`/`DepIssueRoots` (bu koşunun `ComputeDepIssues` sonucu) ile `with` kopyalar;
     `LastDurationMs`, `BuiltContent`, `FedOutputs`, üç döngü alanı aynen) + `run.Scheduler.Complete(id,
     BuildResult.Skipped)`. Yakınsamamış grupta taşınan üye de bugünkü yoldan `Failed`/invalidate'e düşer
     (karar 4; `FailEveryMember` zaten `Result`'ı Failed'a çeker).
  4. Derlenen üyeler için `PersistBuildStateOnSuccess(..., cycle: new CycleMemberRecord(terms[id],
     Flatten(member.ReadStates), fingerprint))` → `BuildState`'in üç alanını yazar; Clean/Build yolları null geçer.
  5. `RecordCycleOutcome(..., compiledCount: members.Count(id => !state[id].Carried))` → decision.log
     `"cycle {leader}: converged (N members, K compiled)"` ve `CycleCompletedEvent(..., CompiledCount: K)`.
- [ ] **Adım 4 — yeşil;** tüm `CycleRoundsTests`, `RunCoordinatorTests`, `EventStreamTests`.
- [ ] **Adım 5 — commit.** `git commit -m "feat(engine): Resolve tur 1 yalniz gereken uyeleri derler; tasinan uye up to date raporlanir"`

### Task 3.5: Doküman

- [ ] ARCHITECTURE §7.3 "Cycles in the signature": bileşik imza DOWNSTREAM için ve "grup kirli mi" için kalır;
  grubun İÇİNDE kimin derleneceğini üye terimi + kayıtlı okunan yüzeyler söyler (yeni paragraf).
- [ ] §8.8 "Cycle rounds": "The first round invokes every member" cümlesi yeniden yazılır: tur 1 yalnız gereken
  üyeyi derler; "gerekli"nin altı kuralı; taşınan üyenin `skipped — up to date` + yeni imzayla deftere yazılması;
  kesilen/yakınsamayan koşuda alanların yazılmaması; bin'deki kardeş kopyalarının tazelenmemesi (karar 5);
  parmak izi. §7.5 ve §16 (`BuildState` alanları). §8.4 ETA: taşınan üyenin tahmini grup bitince düşer (uzun
  tahmin kabul edilen yön). §5.3 `cycleCompleted.compiledCount`. §22 kod haritası (`CycleMemberNeed.cs`,
  `EngineFingerprint.cs`).
- [ ] README "Resolve cycles" anlatımı: "compiles the members that need it — a member whose own inputs and
  the surfaces it read are unchanged is carried".
- [ ] **Faz 3 kapanışı:** tam süit yeşil → merge (`merge: Resolve tur 1 uye duzeyi artimli (perf/resolve-member-skip)`)
  → push → branch sil. **Ölçüm:** S1 (tek üye) taban 63 sn → hedef ≤ 10 sn; S2 (branch) 132 sn → ±%5 değişmez
  (herkes gerekli — kanıt: `decision.log`'daki "round 1 — own inputs changed / read surface moved" satırları);
  ardışık Resolve 1,1 sn değişmez.

---

## Faz 4 — Cycles koşusunda öncelik (`perf/cycles-priority`) — **kullanıcı onayı bekler (karar 11)**

Ölçülmüş gerçek (rapor B3): 4 çekirdeklik başka yük varken Balanced 118,6 sn, Full 80,0 sn; sakin makinede fark
%3–7. Kullanıcının gerçek koşuları (266, 321 sn) bu örüntüde.

### Task 4.1: `PerfProfile.ForRun(mode, profile)` ve ayar

- **Files:** `Core/ProcessControl/PerfProfile.cs`, `Supervisor/RunCoordinator.cs` (`ApplyPerfLocked` çağrıldığı
  yer: profil Cycles'ta dönüştürülür), `App` Ayarlar kartı (yeni anahtar `ResolveAtFullPriority`, `ui-state.json`),
  `Contracts` (`StartRunCommand`'a bayrak — SONA, default `true`), testler: `PerfProfileTests`,
  `RunCoordinatorTests` (cpuGovernor seam: Cycles'ta cap yazılmaz, priority Normal; Build'de profil aynen),
  App ayar realize testi.
- [ ] Kırmızı testler → uygulama → yeşil → doküman (§11.1 profil tablosuna satır, §8.1 Cycles, README perf
  bölümü, konsol notu `parallelism: 4 · cpu cap off · priority normal (Resolve cycles)` — `PerfNoteText` tek kaynak).
- [ ] Commit, merge, push. Ölçüm: yük altında S1 (harness `LOAD=4`) Balanced hedefi ≤ 85 sn.

---

## 5. Ölçüm (kabul kanıtı; eşik gevşetilmez)

Harness aynı makinede: `W=.claude/temp/cycle-resolve-perf-2026-10-02`. Uygulama KAPALI; her ölçüm dizisi:

```bash
python $W/snap.py save <ad>                                   # 26 GB yedek, ~15 sn; kullanıcının çıktıları canlı
printf -- "-clp:PerformanceSummary\r\n" > /d/Projects/Delta/Directory.Build.rsp   # görev kırılımı istenirse
python $W/quiet.py wait 900                                   # Defender sakinleşsin
$W/campaign_hub.sh <tohum-defter> "<etiket>:Balanced:repo"    # S1: tek üye senaryosu (AssemblyInfo.cs'e yorum, toggles)
SEED=<tohum> $W/run.sh <etiket> --pre clean,optimize --mode cycles --perf Balanced   # S3
python $W/analyze.py $W/runs/<etiket> ; python $W/perfsum.py $W/runs/<etiket> --per-invoke
rm -f /d/Projects/Delta/Directory.Build.rsp
python $W/snap.py restore <ad> && python $W/snap.py verify <ad>   # 0 fark bekle; sonra yedeği sil
python $W/touchsrc.py final 'D:\Projects\Delta\OSYS\UsedCars\OSYS.UsedCar\OSYS.UI.General\Properties\AssemblyInfo.cs'
```

`repo` motoru = `src/BuildOrchestrator.Supervisor/bin/Release/...` (önce `dotnet build -c Release`). S2 (branch
değişimi) yeniden üretilemez (o disk durumu geçti); S1 ve S3 yeter.

| Ölçü | Taban | Hedef | Faz |
|---|---|---|---|
| S1 tek üye değişimi, Balanced, sakin | 62–65 sn | ≤ 50 sn (Faz 2) · ≤ 10 sn (Faz 3) | 2, 3 |
| S3 Clean → Optimize → Resolve, Balanced | 115–122 sn | ≤ 105 sn (Faz 2) | 2 |
| Rebuild (154 proje), Balanced | 84 sn | ≤ 78 sn | 2 |
| S1 yük altında (`LOAD=4`), Balanced | 119 sn | ≤ 85 sn | 4 |
| Ardışık Resolve, değişiklik yok | 1,1 sn | değişmez | 3 |
| Çıktı eşdeğerliği (BAML/yüzey, 17 üye) | aynı | aynı (`bamlhash.py`, `SurfaceBench hash`) | 2 |

## 6. Bu planda bilerek OLMAYANLAR

Derleyici sunucusu (ölçüldü: −%10, 2,5–3,6 GB), slot/bariyer değişikliği (klik alt sınırı), "değişen üyeler
önce" sıralaması (≤ 6 sn, ölçülmedi), internal üyeleri yüzeyden çıkarmak (kazanç 0), Defender dışlaması (araç
dışı; −%4…−10 ölçüldü, kullanıcı kararı), OSYS'te döngü kırma ve Copy Local (araç dışı), restore prologu /
hash paralelliği / karar logu (diğer planın E1–E3'ü), atlanan üyenin bin kopyalarını tazeleyen ek hedef
(karar 5), per-type yüzey özeti (tur 2'yi daraltır; ayrı tasarım).

---

## Ek — Diğer oturum için başlangıç prompt'u

```
Build Orchestrator (D:\Projects\Other\Apps\app_build_orchestrator, branch develop) için hazır bir uygulama planını
uygula: `.claude/outputs/2026-10-03-07-15-resolve-cycles-speedup-plan.md`. Önce planı, dayandığı ölçüm raporunu
(`.claude/outputs/2026-10-02-22-15-cycle-resolve-performance-analysis.md`) ve repo CLAUDE.md'yi oku; ARCHITECTURE.md
§7.3, §8.8, §9.2'yi planın gösterdiği satırlardan oku. Plan superpowers:subagent-driven-development ile task task
yürütülür; aynı anda en çok 3 ajan; her task kırmızı test → yeşil → commit; attribution satırı hiçbir yere yazılmaz.

Sıra: Faz 1 (yüzey özeti kör noktaları) → Faz 2 (WPF geçici assembly) → Faz 3 (üye düzeyi artımlı tur 1). Faz 4'e
BAŞLAMA; planın karar 11'i için benden onay iste. Her faz kendi branch'inde biter ve develop'a --no-ff merge + push
edilir; tam süit yeşil olmadan merge yok. Faz 3'e başlamadan önce `.claude/outputs/2026-10-03-04-28-performance-
implementation-plan.md`'nin Faz E'sinin develop'a girip girmediğine bak (aynı dosyaya dokunur); girmediyse önce bana sor.

Ölçüm (planın §5'i) kullanıcının canlı OSYS kopyasında yapılır: uygulama kapalı olmalı, önce `snap.py save`, sonra
`restore` + `verify` (0 fark); `Directory.Build.rsp` geçici konur ve kaldırılır. Ölçümü ajanla değil script'le yap.
Her fazın sonunda kısa bir durum yaz: ne ölçüldü, hedef tuttu mu, ne açık kaldı.
```
