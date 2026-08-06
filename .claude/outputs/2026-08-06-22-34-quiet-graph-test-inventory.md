# Quiet Graph — test envanteri (design v1.3.0 §2.3)

Graf panelinin v1.3.0'a göre sıfırdan yazılmasından ÖNCE, sökülecek davranışı pinleyen testlerin tam
dökümü. Her test üç kutudan birine düşer:

- **YAŞAR** — pinlediği davranış v1.3.0'da aynen geçerli. Dosya taşınsa da iddia değişmez.
- **YENİDEN YAZILIR** — kural bilerek değişti. Test SİLİNMEZ; YENİ kuralı pinleyecek biçimde yeniden
  yazılır ve doc'una **eski iddia + değişme gerekçesi** eklenir (CLAUDE.md). Eşik gevşetmek YASAK.
- **SİLİNİR** — pinlediği MEKANİZMA tasarımdan kalktı, yerine geçen bir kural yok.

Uygulama planı: [2026-08-06-22-34-quiet-graph-tdd-plan.md](2026-08-06-22-34-quiet-graph-tdd-plan.md)

---

## 0. Kapsam

| Dosya | Satır | Test |
|---|---:|---:|
| `App/GraphPanZoomTests.cs` | 913 | 41 |
| `App/GraphRenderTests.cs` | 802 | 47 |
| `App/GraphCullTests.cs` | 621 | 18 |
| `App/GraphCinemaTests.cs` | 517 | 17 |
| `App/GraphCameraTests.cs` | 368 | 29 |
| `App/EdgeStyleResolverTests.cs` | 313 | 24 |
| `App/GraphLayoutTests.cs` | 280 | 18 |
| `App/GraphRealizationPerfTests.cs` | 262 | 3 |
| `App/GraphBinderTests.cs` | 195 | 7 |
| `App/SyntheticGraph.cs` | 131 | fixture |
| `App/GraphClickTests.cs` | 100 | 4 |
| `App/GraphTestView.cs` | 89 | fixture |
| `App/GraphCullingTests.cs` | 66 | 4 |
| **Toplam (graf-özel)** | **4657** | **212** |

Ayrıca grafa DEĞEN komşular (aşağıda §12): `AccessibilityTests`, `CopyTextTests`, `IconGeometryTests`,
`MainWindowInputTests`, `MotionOwnerHygieneTests`, `ReducedMotionCoverageTests`, `ShellLayoutTests`,
`StickyRevealTests`, `SuccessFlourishTests`, `UiResponsivenessBudgetTests`.

`Graph/GraphBuilderTests.cs` (Core, 3 test) ve `Integration/OsysGraphIntegrationTests.cs` **kapsam
dışıdır** — Core'un kenar/katman üretimi değişmiyor.

---

## 1. Sökülen mekanizmalar ve gerekçeleri

Envanterin geri kalanı bu sekiz karara atıf yapar.

| # | Sökülen | Gerekçe |
|---|---|---|
| M1 | Sabit yerleşim (880px tuval, 96px satır, 26px node, `NodeCellWidth`, `MinNodeSpacing`) | §2.3: pitch 44→5 taranır, graf HER panel boyutunda tam sığar. Yerleşim artık panel ölçüsünün fonksiyonu. |
| M2 | Node üstü ad etiketleri + etiket LOD'u (`LabelsFit`, `_labelWidths`, odak muafiyeti, `GraphLabelMetrics`) | §2.3 "Kaldırılanlar". Ad artık hover tooltip'i + seçim etiketiyle verilir. |
| M3 | Kalıcı kenar ağı + kenar stili zinciri (`EdgeStyleResolver`, `GraphEdgeSlot`, akan/hata/sis dalları) | §2.3: "Bağımlılık çizgileri YALNIZ seçimde". 1214 kenarın her 200ms'de stillenmesi de bununla biter. |
| M4 | Kenar sisi (`FogFinishedOpacity`, `fogged`) | M3'ün alt kümesi; sis yalnız kalıcı ağ için vardı. |
| M5 | Cepheyi izleyen kamera (`FitScale`, `ResolveFocus`, `ResolveScale`, `FrontierScale`, Zeno eşikleri, `IsSettled`) | §2.3: kamera YALNIZ seçimle hareket eder. Koşu sırasında kamera durur. |
| M6 | Takip dönüşü + `FOLLOW PAUSED` pili | M5 ile birlikte anlamsız — geri dönülecek otomatik bir hedef yok. |
| M7 | Graf içi dep-issue rozeti (`EnsureBadge`) + `GraphNode.HasDepIssue` | §2.3 "Kaldırılanlar": dep bilgisi kartlarda yaşıyor. Bayrağı grafta okuyan son iki yer M3 ve rozetti. |
| M8 | Viewport cull (`GraphCulling`, `UpdateMaterialization`, `_scannedRegion`, `MaterializeSelection`) + `FullDetailMaxNodes` kapısı | **Cull artık hiçbir şeyi cull edemez** — aşağıya bak. |

### M8 neden ölü (ölçüm değil, aritmetik)

Cull'un işi, "görünür dünya dikdörtgenine değmeyen düğümün UIElement ağacını hiç kurmamak"tı ve
**tek yönlüydü** (bir kez kurulan görsel sökülmez). v1.3.0'da tuval PANELİN KENDİSİDİR: graf her boyutta
panele tam sığar, yani **varsayılan görünümde (zoom 1) her düğüm görünür alandadır**. Dolayısıyla:

1. Açılışta cull hiçbir düğümü elemez — hepsi görünür, hepsi kurulur.
2. Kullanıcı sonradan yakınlaşsa bile materyalizasyon tek yönlü olduğu için kurulmuş ağaç sökülmez;
   kazanılacak yeni bir şey yoktur.

1000 düğümlük bir grafta "her şeyi kurmak" artık bir tercih değil bir GEREKLİLİKTİR — tasarım grafın
tamamının aynı anda okunmasını istiyor. Cull'u tutmak, hiç dallanmayan bir dal ve onu besleyen üç alan
(`_scannedRegion`, `traversing` kolu, `LiveCamera`) demek olurdu. **Kopya/ölü kod yasağı** gereği
siliniyor. Bu, ilk brief'te "kalacak" denen bir maddedir; kararın gerekçesi budur ve geri alınması
ucuzdur (saf aritmetik + bir kapı).

**Sonucu:** `FullDetailMaxNodes` (150) de gider. Onunla birlikte "tek kapı" anlatısı (cull + LOD + sis +
ölçek politikası + jestler hepsi aynı sayıya bağlıydı) tamamen düşer; graf artık HER boyutta aynı
davranır. Jestler de her grafta canlıdır.

### `ClampPan` neden gidiyor

Ayrıntısı planda ("Verilen karar" bölümü): tuval = panel olduğu için kelepçenin "sığan eksen ortalanır"
klozu, ölçek 1'in altındaki her seçimde ötelemeyi grafın merkezine zorlar ve **odakla-sığdır hesabını
tamamen ezer**. Tasarımın kendi kurtarma yolu (boş alana tıkla → varsayılan görünüm) kelepçeyi gereksiz
kılıyor. `Pan`/`ZoomAt`/`RoundPixels` kalır.

---

## 2. `App/GraphLayoutTests.cs` — 18 test · dosya SİLİNİR

Tamamı M1'e bağlı. Yerine `QuietGraphLayoutTests` (13 test) gelir.

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| Layers_are_horizontal_rows_96px_apart_starting_at_the_46px_top_margin | SİLİNİR | M1 — satır aralığı artık pitch | 3 |
| Nodes_in_a_layer_are_spread_symmetrically_around_the_canvas_center | YENİDEN YAZILIR | Simetri iddiası KALIR ama artık bant satırı + blok ortalaması biçiminde → `The_whole_block_is_centred_inside_the_content_box` | 3 |
| Node_spacing_never_exceeds_96px | YENİDEN YAZILIR | Tavan 96 → 44 (§2.3) → `A_roomy_panel_keeps_the_44px_ceiling…` | 3 |
| A_crowded_layer_shrinks_the_spacing_so_the_row_stays_inside_the_canvas | YENİDEN YAZILIR | Kalabalık bant artık SARAR (çok satır), daralmaz → `The_pitch_scan_walks_down_in_half_pixel_steps…` | 3 |
| Canvas_size_is_880_wide_and_covers_every_layer_plus_the_bottom_margin | SİLİNİR | M1 — tuval = panel | 3 |
| A_500_node_layer_keeps_at_least_one_node_width_plus_a_gap_between_neighbouring_centres | SİLİNİR | M1 — üst üste binme artık sarma ile çözülüyor, aralık tabanıyla değil | 3 |
| The_canvas_grows_with_the_widest_layer_so_a_crowded_row_still_fits_inside_it | SİLİNİR | M1 — tuval büyümez, pitch küçülür | 3 |
| A_layer_that_still_fits_the_880_canvas_is_laid_out_exactly_as_before | SİLİNİR | M1 — "eskisi gibi" diye bir taban yok | 3 |
| The_design_sized_36_node_graph_still_lays_out_on_the_original_880px_canvas | SİLİNİR | M1 | 3 |
| The_label_LOD_threshold_is_the_drawn_label_width_not_the_max_width_clamp | SİLİNİR | M2 | 4 |
| The_measured_label_width_is_the_real_drawn_width_and_never_exceeds_the_cell_clamp | SİLİNİR | M2 | 4 |
| A_short_named_layer_of_twelve_keeps_its_labels_because_they_do_not_actually_overlap | SİLİNİR | M2 | 4 |
| Label_overlap_is_scale_invariant_so_a_zoom_threshold_cannot_be_defended | SİLİNİR | M2 | 4 |
| The_layout_reports_the_spacing_of_every_layer_so_the_LOD_decision_has_a_single_source | SİLİNİR | M2 — tek bir pitch var, katman başına aralık yok | 3 |
| Compute_on_an_empty_node_set_still_returns_a_usable_canvas | YENİDEN YAZILIR | İddia KALIR → `An_empty_graph_yields_an_empty_layout_instead_of_throwing` | 3 |
| Edge_control_points_form_a_top_down_cubic_bezier_between_the_two_node_stubs | YENİDEN YAZILIR | Bezier KALIR ama artık seçim kenarına ait ve kontrol noktaları `my=(y1+y2)/2` (JSX:391) → `SelectionEdgeStyle` testi | 8 |
| Edge_geometry_is_a_frozen_stream_geometry_covering_the_curve | YAŞAR | Donmuş geometri disiplini korunuyor (yeni sahibinde) | 8 |
| The_common_prefix_is_stripped_from_node_labels_and_a_non_matching_name_is_left_intact | YENİDEN YAZILIR | `GraphNode.Prefix`/`ShortName` sökülüyor (M2) ama `CommonDotPrefix`/`ShortLabel` ProjectRow + StickyRibbon + RunViewModel için YAŞIYOR → test doğrudan statik yardımcıları hedefler | 4 |

---

## 3. `App/GraphCameraTests.cs` — 29 test

M5 (frontier kamerası) ve `ClampPan` kararı bu dosyanın çoğunu götürüyor.

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| Scale_fits_the_graph_into_the_panel_with_the_30px_padding | SİLİNİR | M5 — sığdırma artık pitch'in işi | 8 |
| Scale_is_clamped_to_the_0_68_floor_on_a_small_panel | SİLİNİR | M5 | 8 |
| Scale_is_clamped_to_the_1_08_ceiling_on_a_huge_panel | SİLİNİR | M5 | 8 |
| Focus_is_the_selected_node_whenever_there_is_a_selection | YENİDEN YAZILIR | Seçimin kamerayı sürüklemesi KALIR ama artık komşularıyla birlikte sığdırılır → `The_focus_set_is_fitted_into_the_panel…` | 8 |
| Focus_is_the_center_of_gravity_of_the_building_frontier_when_nothing_is_selected | SİLİNİR | M5 — koşarken kamera durur | 8 |
| Focus_defaults_to_the_horizontal_center_at_y_equals_H_times_0_3_when_idle | SİLİNİR | M5 — boşta kamera varsayılandır (z1, t0) | 8 |
| Focus_is_the_true_center_once_the_run_is_done_or_stopped | SİLİNİR | M5 — `IsSettled` yok | 8 |
| A_frontier_that_moved_less_than_8px_does_not_retarget_the_camera | SİLİNİR | M5 — Zeno koruması yalnız frontier için vardı | 8 |
| A_frontier_that_moved_at_least_8px_retargets_the_camera | SİLİNİR | M5 | 8 |
| The_small_deviation_threshold_is_only_about_the_frontier_a_selection_always_retargets | SİLİNİR | M5 | 8 |
| The_graph_is_centered_on_both_axes_when_it_fits_entirely_inside_the_panel | SİLİNİR | `ClampPan` kararı | 8 |
| Panning_keeps_a_12px_margin_at_the_leading_edge | SİLİNİR | `ClampPan` kararı | 8 |
| Panning_keeps_a_12px_margin_at_the_trailing_edge | SİLİNİR | `ClampPan` kararı | 8 |
| Tx_and_ty_are_rounded_to_whole_pixels_js_math_round_parity | YENİDEN YAZILIR | `RoundPixels` KALIR; test artık `FocusAndFit` üzerinden koşar (ClampPan yok) | 8 |
| Outside_cinema_the_scale_is_always_the_fit_scale | SİLİNİR | M5 + M8 — sinema kapısı yok | 8 |
| A_selection_beats_the_frontier_and_ignores_the_zeno_guard_in_cinema | SİLİNİR | M5 | 8 |
| A_single_building_node_frames_at_the_follow_ceiling | SİLİNİR | M5 | 8 |
| A_wide_frontier_clamps_at_the_follow_floor | SİLİNİR | M5 | 8 |
| The_frontier_margins_are_derived_from_the_layout_cell_and_the_fit_padding | SİLİNİR | M5 + M1 | 8 |
| The_frontier_frame_includes_the_cell_margins_and_fit_padding | SİLİNİR | M5 | 8 |
| The_frontier_frame_applies_the_horizontal_margin_on_a_narrow_panel | SİLİNİR | M5 | 8 |
| Settled_or_idle_cinema_returns_to_the_overview_fit_scale | SİLİNİR | M5 | 8 |
| A_scale_change_below_the_threshold_keeps_the_previous_scale_zeno_guard | SİLİNİR | M5 | 8 |
| The_cinema_scale_values_are_pinned_to_their_spec_numbers | YENİDEN YAZILIR | Sayı pinleme disiplini KALIR, sayılar değişir: seçim 0.7–2.6, manuel 0.7–5.0, adım 1.14, geçiş 460/160 | 8 |
| Compute_with_an_explicit_scale_centers_the_focus_and_the_3_arg_overload_stays_fit | SİLİNİR | M5 — `Compute` aşırı yüklemeleri yok | 8 |
| Zooming_at_the_cursor_keeps_the_world_point_under_it_fixed | YENİDEN YAZILIR | İddia KALIR (imleç ankrajı), band 0.45–2.0 → 0.7–5.0 ve adım 1.1 → 1.14 | 9 |
| Manual_zoom_is_clamped_to_the_manual_band | YENİDEN YAZILIR | Band değişti (§2.3 "Wheel = zoom 0.7–5.0") | 9 |
| Panning_moves_the_camera_and_stays_inside_the_12px_margins | YENİDEN YAZILIR | Pan KALIR, kelepçe gider → `Pan` artık deltayı ekler ve yuvarlamaz | 9 |
| Zooming_out_below_the_fit_band_centers_the_axis_that_now_fits | SİLİNİR | `ClampPan` kararı | 9 |

---

## 4. `App/GraphCinemaTests.cs` — 17 test · **dosya tamamen SİLİNİR**

Dosyanın TAMAMI M2 (etiket muafiyeti) + M4 (sis) + M5 (follow-zoom) üzerinedir. v1.3.0'da bunların
hiçbiri yoktur; yerine geçen bir kural da yoktur.

| Test | Karar | Gerekçe |
|---|---|---|
| A_large_graph_fogs_its_idle_edges_to_the_dim_level | SİLİNİR | M4 |
| A_small_graph_keeps_todays_full_opacity_edges | SİLİNİR | M4 |
| A_building_frontier_zooms_the_camera_into_the_follow_band | SİLİNİR | M5 |
| Settled_returns_the_camera_to_the_overview_fit | SİLİNİR | M5 |
| A_small_graph_never_changes_scale_when_building_todays_behavior_pinned | SİLİNİR | M5 + M8 |
| A_selection_zooms_to_the_selection_scale_in_cinema | SİLİNİR | Sabit 1.1 ölçek yok; yerine odakla-sığdır (Task 8'de kendi testleri) |
| Only_a_FRONTIER_scale_is_remembered_so_it_cannot_suppress_the_next_frontier_retarget | SİLİNİR | M5 |
| A_small_graph_never_latches_a_follow_scale_the_cinema_gate_closes_the_latch_too | SİLİNİR | M5 + M8 |
| A_building_node_is_named_even_where_its_layers_labels_overlap | SİLİNİR | M2 |
| The_selected_node_is_named_even_where_its_layers_labels_overlap | SİLİNİR | M2 — seçili düğümün adı artık overlay etiketiyle verilir (Task 8'de kendi testi) |
| A_building_node_is_named_even_while_the_camera_is_manual | SİLİNİR | M2 |
| A_finished_node_gives_its_label_back_because_the_exemption_is_not_a_latch | SİLİNİR | M2 |
| The_neighbours_of_the_selected_node_stay_unnamed_because_the_exemption_does_not_spread | SİLİNİR | M2 |
| The_layer_decision_is_scale_invariant_so_zooming_neither_wins_nor_loses_labels | SİLİNİR | M2 |
| A_frontier_swap_that_does_not_move_the_camera_still_refreshes_the_labels | SİLİNİR | M2 + M5 |
| Small_graph_labels_are_untouched_by_the_label_decision_machinery | SİLİNİR | M2 + M8 |
| A_node_materialised_before_the_first_camera_target_lands_on_the_right_decision… | SİLİNİR | M2 + M8 |

---

## 5. `App/EdgeStyleResolverTests.cs` — 24 test · **dosya tamamen SİLİNİR**

Tamamı M3/M4. Yerine `SelectionEdgeStyle`'ın TEK stili ve onun 3 testi gelir (dash bölme, offset,
bezier). Silinen 24 testin pinlediği zincir (default/succeeded/failed/bad/hot/fog dalları) v1.3.0'da
YOKTUR: seçim kenarının tek bir görünümü vardır — amber akan kesik, 1.2px, opacity 0.75.

Not: **dash'in kalınlığa BÖLÜNMESİ** kuralı (`The_flowing_dash_draws_the_same_absolute_4px_7px_pattern_at_both_thicknesses`)
kavram olarak yaşıyor — `SelectionEdgeStyle` 4/8 desenini 1.2'ye böler ki mutlak desen 4px/8px kalsın.
Testi yeni sahibinde YENİDEN YAZILIYOR sayılır (bu satır o devri kaydeder).

---

## 6. `App/GraphCullTests.cs` — 18 test

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| A_graph_inside_the_eager_band_still_materialises_every_node_and_edge | YENİDEN YAZILIR | M8 — "eager band" yok; artık HER graf her düğümü kurar (kenar hariç, M3) | 4 |
| The_design_sized_graph_is_built_exactly_as_before_the_cull_full_tree_labels_and_no_dead_badges | SİLİNİR | M2 + M7 + M8 — üç dayanağı da gitti | 4 |
| A_small_but_crowded_graph_keeps_every_label_because_LOD_shares_the_full_detail_gate | SİLİNİR | M2 + M8 | 4 |
| Above_the_gate_short_labels_survive_a_crowded_layer_and_long_ones_do_not | SİLİNİR | M2 | 4 |
| One_graph_drops_the_crowded_layers_labels_and_keeps_the_sparse_layers_in_the_same_pass | SİLİNİR | M2 | 4 |
| A_node_that_lost_its_label_carries_the_full_project_name_as_a_tooltip_without_building_objects | YENİDEN YAZILIR | Tooltip ANA isim yolu oldu; "düğüm başına nesne kurulmaz" iddiası da KALIR (overlay tek örnek) | 7 |
| A_large_graph_only_builds_the_visual_tree_of_the_nodes_the_camera_can_see | SİLİNİR | M8 | 4 |
| Enlarging_the_viewport_materialises_the_nodes_that_scroll_into_view | SİLİNİR | M8 — panel büyümesi artık yerleşimi yeniden hesaplar (Task 4'ün kendi testi) | 4 |
| A_node_whose_status_changed_while_it_was_culled_shows_the_new_status_when_it_appears | SİLİNİR | M8 | 4 |
| The_selected_node_and_its_neighbours_are_never_culled_even_when_the_camera_is_elsewhere | SİLİNİR | M8 | 4 |
| An_edge_is_styled_from_the_models_even_when_the_node_at_its_end_is_culled | SİLİNİR | M3 + M8 | 8 |
| Snapping_to_a_far_viewport_never_materialises_the_nodes_in_between | SİLİNİR | M8 | 4 |
| An_animated_pan_does_materialise_the_band_it_travels_through_because_those_nodes_are_really_seen | SİLİNİR | M8 | 4 |
| A_node_materialised_while_the_reveal_is_playing_joins_the_stagger_instead_of_popping_in | SİLİNİR | M8 — geç materyalizasyon yok, tüm düğümler `SetGraph`'ta doğar ve dalgaya birlikte girer | 9 |
| Pushing_the_same_statuses_again_touches_no_node_at_all | YAŞAR | "Değişmediyse dokunma" hızlı yolu korunuyor ve opaklık sisteminin Zeno korumasının temeli | 5 |
| Dashed_frames_share_one_frozen_collection_instead_of_allocating_per_node_per_tick | YAŞAR | discovered'ın kesikli çerçevesi korunuyor; paylaşımlı donmuş koleksiyon disiplini de | 4 |
| A_lazily_built_node_and_badge_resolve_their_tokens_through_the_real_app_resource_chain | YENİDEN YAZILIR | "Tembel" ve "rozet" düştü (M7/M8) ama **token zinciri realize testi** kritik → düğüm için yeniden yazılır | 4 |
| A_lazily_materialised_node_lands_under_the_node_layer_so_edges_stay_beneath_it | YENİDEN YAZILIR | Z-order iddiası KALIR (seçim kenarları düğümlerin ALTINDA), "tembel" düşer | 8 |

---

## 7. `App/GraphCullingTests.cs` — 4 test · **dosya SİLİNİR**

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| The_visible_world_rect_is_the_inverse_of_the_camera_transform_plus_a_row_of_margin | SİLİNİR | M8 | 4 |
| An_unmeasured_panel_or_a_zero_scale_camera_yields_an_empty_rect_instead_of_a_bogus_one | SİLİNİR | M8 | 4 |
| Node_bounds_cover_the_label_cell_not_just_the_26px_square | SİLİNİR | M1 + M2 + M8 | 4 |
| Edge_bounds_contain_the_whole_curve_because_a_bezier_never_leaves_its_control_hull | SİLİNİR | M3 + M8 | 8 |

---

## 8. `App/GraphRenderTests.cs` — 47 test

En karışık dosya: düğüm görseli (M1/M2/M7 ile gider), kenar/dash makinesi (M3 ile gider), reveal ve
motion hijyeni (YAŞAR), kamera kablajı (M5 ile değişir).

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| A_node_is_a_26px_square_with_a_4px_corner_radius | YENİDEN YAZILIR | M1 — kenar artık `pitch×0.6`; 4px radius (Radius.Sm) KALIR | 4 |
| A_discovered_node_gets_a_dashed_frame_wpf_border_cannot_dash_so_it_is_a_rectangle | YAŞAR | §2.3: "discovered = kesikli `--border-strong`" | 4 |
| The_node_label_is_the_short_name_in_10px_mono_with_a_LOCAL_Ideal_formatting_mode | SİLİNİR | M2 | 4 |
| Selecting_a_node_shows_its_amber_ring_and_thickens_the_square_border_to_2px | YAŞAR | §2.3: "Seçili node'da 2px `--focus-ring` outline (offset 2)" | 8 |
| The_node_icon_is_scaled_to_13px_and_centred_inside_the_26px_square | YENİDEN YAZILIR | Oran 0.5 → **0.52** ve boyut canlı (§2.3: "node'un %52'si, 1.8px stroke") | 4 |
| A_dep_issue_node_gets_a_13px_circle_badge_holding_a_filled_red_triangle | SİLİNİR | M7 | 4 |
| A_node_that_gains_a_dep_issue_later_builds_its_badge_on_demand_and_hides_it_again | SİLİNİR | M7 | 4 |
| The_node_icon_and_the_dep_badge_are_the_dictionary_geometries_not_copies_parsed_in_code | YENİDEN YAZILIR | Rozet kolu düşer; **ikonun sözlükten gelmesi** iddiası KALIR (kopya yasağı guard'ı) | 4 |
| Node_and_edge_colours_are_resolved_from_the_foundation_token_brushes_not_hardcoded_hex | YENİDEN YAZILIR | Kenar kolu seçim kenarına daralır; düğüm kolu aynen KALIR | 4 / 8 |
| The_layer_stagger_is_55ms_per_layer_capped_at_330ms | YENİDEN YAZILIR | §2.3: gecikme KATMAN değil **build-order index × 9ms, tavan 520ms** | 9 |
| Nodes_start_fully_transparent_so_the_staggered_reveal_never_flashes | YAŞAR | Motion sözleşmesi | 9 |
| Reduced_motion_places_the_nodes_instantly_with_no_stagger | YAŞAR | Global Constraint | 9 |
| The_reveal_stagger_takes_the_sync_reveal_hero_while_it_plays | YAŞAR | Hero mutex'i korunuyor (liste ile ORTAK) | 9 |
| The_reveal_releases_the_sync_reveal_hero_when_it_completes | YAŞAR | " | 9 |
| A_stale_reveal_completion_does_not_release_the_current_reveal_hero | YAŞAR | " | 9 |
| A_reveal_yields_to_an_already_running_hero_and_places_the_nodes_instantly | YAŞAR | " | 9 |
| Re_SetGraph_releases_the_previous_reveal_hero_before_taking_it_again | YAŞAR | " | 9 |
| Flowing_edges_are_UIElement_paths_bound_to_one_single_shared_dash_clock | YENİDEN YAZILIR | M3 — akan kenar yalnız seçimde var; **tek paylaşımlı clock** disiplini KALIR | 8 |
| The_shared_dash_animation_loops_two_full_periods_at_30fps | YENİDEN YAZILIR | Yeni desen 4/8, yol −24, süre 640ms (§2.3); 30fps tavanı KALIR | 8 |
| A_selected_1_6px_flowing_edge_divides_its_dash_and_gets_the_thick_branch_of_the_same_clock | YENİDEN YAZILIR | Tek kalınlık kaldı (1.2px); **bölme** kuralı KALIR, iki-dal makinesi düşer | 8 |
| A_selected_1_6px_static_error_edge_divides_its_dash_and_never_animates | SİLİNİR | M3 — statik hata kenarı yok | 8 |
| The_shared_clock_is_released_when_the_last_flowing_edge_stops_and_rebuilt_on_demand | YENİDEN YAZILIR | Seçim kalkınca bırakılır | 8 |
| Unloading_the_view_releases_a_running_shared_dash_clock | YAŞAR | Clock hijyeni (yeni sahiplerde: seçim kenarı + beads) | 6 / 8 |
| Reduced_motion_keeps_the_dash_but_never_starts_a_clock | YENİDEN YAZILIR | §2.3: reduced-motion'da beads VE akan çizgiler tamamen KAPALI — "dash kalır" kolu değişir | 6 / 8 |
| A_building_node_breathes_1_to_half_and_back_over_1_6s_at_30fps | YENİDEN YAZILIR | Nabız → **beads** (§2.3 yeni building animasyonu); 30fps tavanı KALIR | 6 |
| The_pulse_stops_when_the_node_leaves_building | YENİDEN YAZILIR | Beads karşılığı: bitişte 640ms'de söner, 700ms daha döner | 6 |
| Re_SetGraph_stops_the_pulse_on_the_discarded_old_visuals | YENİDEN YAZILIR | Beads clock'u için aynı hijyen | 6 |
| Reduced_motion_never_starts_the_building_pulse | YENİDEN YAZILIR | Beads karşılığı | 6 |
| Flipping_the_motion_signal_at_runtime_stops_the_flow_and_the_pulse_immediately | YENİDEN YAZILIR | Canlı reduced-motion KALIR; hedefler beads + seçim kenarı olur | 6 |
| Selection_dims_every_non_neighbour_node_to_25_percent_and_untouched_edges_to_16_percent | YENİDEN YAZILIR | §2.3: odak dışı **0.1**; kenar kolu düşer (M3) | 5 / 8 |
| Clearing_the_selection_restores_every_node_and_edge | YENİDEN YAZILIR | Kenar kolu → kenarlar SÖKÜLÜR; düğüm kolu koşu kararına döner | 8 |
| The_panel_header_counts_projects_and_dependencies_from_the_data | YAŞAR | Başlık sayacı değişmiyor (§2.3 ilk cümle) | — |
| The_panel_header_is_28px_over_surface_with_a_border_subtle_bottom_line | YAŞAR | " | — |
| Before_sync_the_ground_shows_the_dashed_empty_state_box | YAŞAR | Boş durum değişmiyor | — |
| The_camera_uses_a_scale_plus_translate_transform_group_and_targets_the_selected_node | YENİDEN YAZILIR | Transform grubu KALIR; hedef artık odak KÜMESİNİN sığdırması | 8 |
| Reduced_motion_snaps_the_camera_with_no_animation | YAŞAR | Global Constraint | 8 |
| With_motion_enabled_the_camera_animates_over_460ms | YAŞAR | §2.3: 460ms ease-in-out | 8 |
| An_unchanged_camera_target_does_not_restart_the_460ms_transition | YAŞAR | Zeno koruması (kamera hedefi) korunuyor | 8 |
| Only_a_FRONTIER_focus_is_remembered_so_it_cannot_suppress_the_next_frontier_retarget | SİLİNİR | M5 | 8 |

> Yukarıda 39 satır var; kalan 8 test `FakeMotionSettings` yardımcı sınıfı ve `[Theory]` veri
> üreticileridir — sahipleriyle birlikte taşınır/silinir.

---

## 9. `App/GraphPanZoomTests.cs` — 41 test

Jest çekirdeği YAŞAR; follow/pil bölümü (M6) tamamen gider; `_cullEnabled` kapısına bağlı testler M8 ile
gider.

**YAŞAR (7)** — jestin kendisi v1.3.0'da aynen var:
`A_drag_beyond_the_threshold_pans_the_camera_and_enters_manual_mode` (eşik 3px'e YENİDEN YAZILIR),
`Each_move_pans_by_its_OWN_delta_so_the_point_under_the_hand_tracks_the_graph`,
`During_a_drag_the_ground_shows_the_hand_cursor_and_releases_it_after`,
`Grabbing_the_graph_mid_flight_freezes_the_current_frame_not_the_animation_target`,
`Losing_the_capture_cancels_the_gesture_instead_of_counting_as_a_release`,
`A_new_topology_cancels_an_in_flight_gesture_and_leaves_manual_mode`,
`In_cinema_pressing_the_ground_keeps_the_selection_until_the_release` (ad "cinema" nitelemesinden
arındırılarak — artık TEK davranış).

**YENİDEN YAZILIR (6):**

| Test | Gerekçe |
|---|---|
| A_subthreshold_press_release_on_the_ground_clears_the_selection_without_entering_manual_mode | Eşik platformdan → **3px** (§2.3); ayrıca seçim yokken aynı tıklama görünümü VARSAYILANA döndürür |
| The_wheel_zooms_at_the_cursor_and_enters_manual_mode | Band/adım değişti; "manuel mod" kavramı artık yalnız "kamera kullanıcıda" demek (geri dönüş yok) |
| A_negative_wheel_delta_zooms_out_by_the_same_step | Adım 1.1 → 1.14 |
| Every_gesture_step_stamps_a_manual_input_so_a_slow_drag_cannot_be_interrupted | "Kesinti" artık yalnız statü tick'inden gelmez (M5) — test, sürükleme sırasında kameranın dışarıdan hedeflenmediğini pinler |
| Manual_mode_suppresses_automatic_retargeting | Tek otomatik hedefleme SEÇİMDİR; test onu pinler |
| A_panel_resize_still_materialises_while_the_camera_is_manual | M8 — "materialise" yerine **yeniden yerleşim** (Task 4 ile kesişir) |

**SİLİNİR (28):** M6'nın tamamı (`Follow_resumes_only_after_the_delay…`,
`Manual_camera_persists_while_there_is_nothing_to_follow`, `A_selection_counts_as_a_follow_target`,
`Selecting_a_project_resumes_follow_immediately…`, `Clearing_the_selection_does_not_end_manual_mode…`,
`Leaving_manual_mode_retargets_the_focus…`, `Leaving_manual_mode_retargets_the_scale…`,
`A_long_drag_refreshes_the_stamp_but_arms_the_resume_trigger_once`,
`A_tick_that_lands_before_the_delay_rearms_for_the_remainder`,
`A_held_gesture_never_hands_the_camera_back_and_the_trigger_does_not_spin`,
`A_selection_arriving_mid_drag_does_not_leave_the_gesture_panning_outside_manual_mode`,
`Nothing_to_follow_means_no_trigger_at_all_so_the_view_stays_asleep`,
`A_run_that_starts_while_the_camera_is_manual_revives_the_resume_trigger`,
`A_gesture_cancelled_by_a_capture_loss_still_leaves_a_live_resume_trigger`,
`A_new_topology_stops_the_pending_trigger_and_hides_the_pill`,
`An_empty_graph_hides_the_pill_even_though_the_camera_never_runs`,
`The_pill_sits_just_left_of_the_counter_so_the_machine_output_never_moves`,
`Unloading_the_view_stops_the_pending_trigger_so_the_dispatcher_cannot_root_it`,
`The_pill_shows_while_follow_is_suspended_and_a_click_resumes_immediately`,
`The_pill_stays_hidden_while_there_is_nothing_to_follow`,
`The_pill_hides_again_when_the_run_ends_while_the_camera_is_still_manual`,
`The_pill_carries_the_shared_copy_and_the_uia_name`,
`The_pill_realises_in_a_real_window_with_its_tokens_resolved`) —
artı M8'e bağlı olanlar (`Gestures_are_inert_outside_cinema`, `Gestures_are_inert_before_the_camera_has_a_target`,
`Zooming_out_materialises_the_newly_visible_nodes_even_with_the_run_finished`,
`Dragging_materialises_the_nodes_the_pan_brings_into_view_even_with_the_run_finished`).

> **Dikkat:** `Unloading_the_view_stops_the_pending_trigger…` timer sızıntısı hijyenini pinliyordu. Timer
> ortadan kalktığı için test siliniyor; ama AYNI hijyen sınıfı (dispatcher/clock köklemesi) beads clock'u
> ve seçim kenarı clock'u için Task 6/8'de YENİDEN kuruluyor — hijyen kaybolmuyor, sahibi değişiyor.

---

## 10. `App/GraphRealizationPerfTests.cs` — 3 test · hepsi YAŞAR (ikisi yeniden ölçülür)

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| Realizing_the_design_sized_graph_stays_under_the_budget_and_500_and_1000_are_recorded | YENİDEN YAZILIR | Bütçe KALIR; **cull kalktığı için 500/1000 artık TAMAMEN kurulur** — ölçüm yeniden alınır ve doc'una eski rejim (cull'lu açılış) + değişme gerekçesi yazılır. Bütçeyi gevşetmek YASAK: aşarsa tasarım/uygulama düzeltilir | 4 |
| A_graph_node_builds_no_more_than_the_per_node_object_ceiling | YENİDEN YAZILIR | Tavan **DÜŞER** (etiket + rozet + nabız kabı + Viewbox gitti). Yeni tavan ölçülür ve yazılır | 4 |
| A_500_node_graph_realizes_in_a_real_window_through_the_app_resource_chain | YAŞAR | Realize disiplini; ek olarak **resize altında** realize vakası eklenir | 4 |

---

## 11. Küçük dosyalar

### `App/GraphClickTests.cs` — 4 test

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| Clicking_a_node_selects_it_and_clicking_the_same_node_again_clears_the_selection | YAŞAR | §3.3 aynen | 8 |
| Clicking_a_different_node_moves_the_selection_instead_of_clearing_it | YAŞAR | §3.3 | 8 |
| Clicking_the_empty_ground_clears_the_selection | YENİDEN YAZILIR | §2.3'te iki kollu: seçim VARSA bırakılır, YOKSA görünüm varsayılana döner | 8 |
| A_click_on_a_node_is_handled_so_it_never_reaches_the_ground_and_undoes_itself | YAŞAR | Aynı tuzak, aynı koruma | 8 |

### `App/GraphBinderTests.cs` — 7 test

| Test | Karar | Gerekçe | Task |
|---|---|---|---|
| Edges_point_from_dependency_to_dependent | YAŞAR | Kenar yönü deps/dependents haritasını besliyor | 8 |
| Layer_falls_back_to_topological_depth_when_no_layer_patterns_are_configured | YAŞAR | Bantlar katmandan | 3 |
| Cycle_members_get_finite_back_edge_trimmed_depths_not_a_shared_component_depth | YAŞAR | " | 3 |
| Cycle_members_are_reported_as_cycle_status | YAŞAR | Cycle tonu §2.3 tablosunda | 4 |
| Nodes_source_the_dep_badge_from_row_HasDepIssue | SİLİNİR | M7 — `GraphNode.HasDepIssue`'nun grafta son iki okuyucusu (rozet ve kenar stili) gitti; alan da sökülüyor | 4 |
| Node_order_within_a_layer_follows_build_order | YAŞAR | **Kritik**: bant içi sıra = build-order (§2.3) ve açılış dalgası da onu izler | 3 / 9 |
| Short_label_strips_the_common_prefix_derived_from_the_data_not_a_hardcoded_one | YENİDEN YAZILIR | `GraphNode.Prefix`/`ShortName` sökülüyor; `CommonDotPrefix`/`ShortLabel` ProjectRow + StickyRibbon + RunViewModel için YAŞIYOR → test statik yardımcıları doğrudan hedefler | 4 |

### Ortak fixture'lar

| Dosya | Karar | Ne değişir |
|---|---|---|
| `App/GraphTestView.cs` | YAŞAR | `New`/`Sized`/`Resize`/`Realized` dörtlüsü korunur ve **`Resize` Task 4'ün ana yolu olur**. `labelFontFamily` parametresi SÖKÜLÜR (M2 — etiket ölçümü yok). Merge zinciri (Motion → Tokens → Icons) aynen kalır. |
| `App/SyntheticGraph.cs` | YENİDEN YAZILIR | Üretim profili (177/6 katman) ve determinizm KALIR. `NamePrefix` ile `GraphNode(..., NamePrefix)` argümanı düşer (M2), dep-issue oranı düşer (M7). |

---

## 12. Komşular (grafa değen ama grafa ait olmayan dosyalar)

| Dosya | Karar | Ne olur |
|---|---|---|
| `AccessibilityTests.cs` | YENİDEN YAZILIR | `GraphNodeBody` peer'ı + `AccessibilityNames.GraphNode(ad, statü)` **YAŞAR** (düğümde ad olmasa da UIA adı taşır). `GraphListSplitter` YAŞAR. `GraphFollowPill`'e dair her şey SİLİNİR (M6). |
| `CopyTextTests.cs` | YAŞAR | `DEPENDENCY GRAPH` başlığı değişmiyor; **ipucu satırı metni için yeni bir pin EKLENİR** (Task 9). |
| `IconGeometryTests.cs` | YENİDEN YAZILIR | `Icon.Package` YAŞAR; `Icon.DepWarn`'ın graf tüketicisi düşer (M7) — ikon sözlükte kalır (liste kartı okuyor), yalnız "GraphView de okuyor" iddiası çıkar. |
| `MainWindowInputTests.cs` | YAŞAR | Layout modu ↔ graf görünürlüğü iddiaları aynen; Task 2 buna DAVRANIŞ EKLER, görünürlüğü değiştirmez. |
| `MotionOwnerHygieneTests.cs` | YAŞAR | `GraphView`'ın latch-first sapması korunuyor (MainWindow ona dayanıyor). |
| `ReducedMotionCoverageTests.cs` | YENİDEN YAZILIR | `GraphView_keeps_no_dash_clock_no_pulse_and_an_instant_reveal…` → nabız yerine **beads**, kenar dash'i yerine **seçim kenarı**; reveal iddiası aynen kalır. |
| `ShellLayoutTests.cs` | YAŞAR | Panel görünürlüğü/oranları değişmiyor. |
| `StickyRevealTests.cs` | YAŞAR | Graf reveal ↔ liste reveal ORTAK hero (`GraphView.RevealHeroKey`) korunuyor. |
| `SuccessFlourishTests.cs` | YENİDEN YAZILIR | `Graph_nodes_release_their_clocks_when_they_succeed_instead_of_celebrating` → nabız clock'u yerine **beads clock'u** (spin-down dâhil). `SourceGuard.ScanText` kaynak listesindeki silinmiş dosya adları TEMİZLENİR — aksi halde guard "taradığını iddia ettiği dosyayı bulamadı" diye kırmızı verir (kasıtlı kontrol). |
| `UiResponsivenessBudgetTests.cs` | YAŞAR | Statü tick bütçesi aynen; Task 5 ve 6'nın perf testleri **aynı bütçe sabitini** okur (kopya yasak). |

---

## 13. Özet

| Dosya | Test | Yaşar | Yeniden yazılır | Silinir |
|---|---:|---:|---:|---:|
| GraphLayoutTests | 18 | 1 | 6 | 11 |
| GraphCameraTests | 29 | 0 | 6 | 23 |
| GraphCinemaTests | 17 | 0 | 0 | **17 (dosya)** |
| EdgeStyleResolverTests | 24 | 0 | 1 | **23 (dosya)** |
| GraphCullTests | 18 | 2 | 4 | 12 |
| GraphCullingTests | 4 | 0 | 0 | **4 (dosya)** |
| GraphRenderTests | 39 | 13 | 20 | 6 |
| GraphPanZoomTests | 41 | 7 | 6 | 28 |
| GraphRealizationPerfTests | 3 | 1 | 2 | 0 |
| GraphClickTests | 4 | 3 | 1 | 0 |
| GraphBinderTests | 7 | 5 | 1 | 1 |
| **Toplam** | **204** | **32** | **47** | **125** |

Üç dosya tamamen kalkıyor (`GraphCinemaTests`, `EdgeStyleResolverTests`, `GraphCullingTests`) ve
`GraphLayoutTests` yerini `QuietGraphLayoutTests`'e bırakıyor. Yerine gelen yeni dosyalar:
`GraphVisibilityTests`, `QuietGraphLayoutTests`, `QuietGraphNodeTests`, `GraphNodeOpacityTests`,
`GraphRunLifecycleTests`, `GraphBeadsTests`, `GraphBeadsWiringTests`, `GraphOverlayTests`,
`GraphHoverTests`, `GraphSelectionFocusTests`, `GraphRevealTests`, `GraphNavigationTests`.

## 14. Yerine hiçbir test gelmeyen davranışlar

Bu listedeki her satır, **bilinçli olarak** bir daha korunmayacak bir davranıştır. Review'da tek tek
okunur; birinin geri istenmesi bir TASARIM kararıdır, bir hata düzeltmesi değil.

1. **Kalıcı bağımlılık çizgi ağı.** Seçim yokken graf çizgisizdir. Bir projenin komşuluklarını görmek
   için onu SEÇMEK gerekir.
2. **Kenar stiliyle anlatılan koşu hikâyesi** — akan amber kenar, yeşil/kırmızı biten dallar, hatayı
   taşıyan statik kırmızı kesik, sis kademeleri. Koşu artık yalnız DÜĞÜM renkleri ve opaklıkla anlatılır.
3. **Node üstü ad etiketleri ve etiket LOD'u.** Hiçbir düğüm adını üstünde taşımaz; ad yalnız hover ve
   seçimle görünür. Ekran okuyucu adı UIA'dan almaya devam eder.
4. **Graf içi dep-issue rozeti.** Dep bilgisi yalnız liste kartlarında.
5. **Cepheyi izleyen kamera.** Koşu sırasında kamera hiç hareket etmez; kullanıcı nereye baktıysa orada
   kalır.
6. **Takip dönüşü ve `FOLLOW PAUSED` pili.** Otomatik kamera olmadığı için "geri dönülecek" bir durum
   yok. Panel başlığında pil yok.
7. **Viewport cull ve `FullDetailMaxNodes` kapısı.** Graf artık her boyutta tamamen kurulur; 150 düğümün
   üstünde davranış değiştiren bir eşik yok.
8. **Pan kelepçesi (12px kenar payı).** Kullanıcı grafı panelin dışına sürükleyebilir; kurtarma jesti boş
   alana tıklamaktır.
9. **Zeno korumalarının frontier/ölçek kolları.** Kamera hedefi Zeno koruması (aynı hedefe yeniden
   animasyon başlatmama) KALIR; frontier ağırlık merkezi ve ölçek latch'leri gider.
10. **`GraphNode.HasDepIssue` / `Prefix` / `ShortName` alanları.** Graf modeli yalnız ad + katman +
    statü taşır.
