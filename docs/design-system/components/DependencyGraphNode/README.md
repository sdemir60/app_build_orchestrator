# DependencyGraphNode

Bağımlılık grafiği düğümü: renk = görsel durum (projenin çıktısının durumu); building nabız atar; seçili düğümde amber halka.

**Tüketici sağlar:** `label` (proje adı), `status`, seçim durumu, `onClick`; konumlandırma ve kenarlar kapsayıcının işidir.

Uygulamada başlangıç modunda (`Not synced`) kenar kesiklidir; döngü üyesinin içindeki küp her durumda amber kalır. `skipped` ve `cycle` bundle'da durur ama uygulamada bir düğüm rengi değildir.

**Props:** `label` · `status` · `selected` · `size` (px, 30) · `showLabel` · `onClick` · `style`.
