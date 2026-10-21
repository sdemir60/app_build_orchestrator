/* @ds-bundle: {"format":3,"namespace":"DeltaBuildOrchestratorDS_eb0bd1","components":[{"name":"Button","sourcePath":"components/controls/Button.jsx"},{"name":"Chip","sourcePath":"components/controls/Chip.jsx"},{"name":"IconButton","sourcePath":"components/controls/IconButton.jsx"},{"name":"Kbd","sourcePath":"components/controls/Kbd.jsx"},{"name":"Segment","sourcePath":"components/controls/Segment.jsx"},{"name":"ConsoleLine","sourcePath":"components/data/Console.jsx"},{"name":"Console","sourcePath":"components/data/Console.jsx"},{"name":"Metric","sourcePath":"components/data/Metric.jsx"},{"name":"ProgressBar","sourcePath":"components/data/ProgressBar.jsx"},{"name":"ProjectRow","sourcePath":"components/data/ProjectRow.jsx"},{"name":"Tag","sourcePath":"components/data/Tag.jsx"},{"name":"Checkbox","sourcePath":"components/forms/Checkbox.jsx"},{"name":"Field","sourcePath":"components/forms/Field.jsx"},{"name":"Input","sourcePath":"components/forms/Input.jsx"},{"name":"Select","sourcePath":"components/forms/Select.jsx"},{"name":"Switch","sourcePath":"components/forms/Switch.jsx"},{"name":"DependencyGraphNode","sourcePath":"components/graph/DependencyGraphNode.jsx"},{"name":"Dialog","sourcePath":"components/shell/Dialog.jsx"},{"name":"StatusBar","sourcePath":"components/shell/StatusBar.jsx"},{"name":"StatusBarItem","sourcePath":"components/shell/StatusBar.jsx"},{"name":"Tabs","sourcePath":"components/shell/Tabs.jsx"},{"name":"TitleBar","sourcePath":"components/shell/TitleBar.jsx"},{"name":"Toast","sourcePath":"components/shell/Toast.jsx"},{"name":"Toolbar","sourcePath":"components/shell/Toolbar.jsx"},{"name":"ToolbarSep","sourcePath":"components/shell/Toolbar.jsx"},{"name":"ToolbarSpacer","sourcePath":"components/shell/Toolbar.jsx"},{"name":"Tooltip","sourcePath":"components/shell/Tooltip.jsx"},{"name":"Spinner","sourcePath":"components/status/Spinner.jsx"},{"name":"StatusBadge","sourcePath":"components/status/StatusBadge.jsx"},{"name":"STATUS_META","sourcePath":"components/status/StatusGlyph.jsx"},{"name":"StatusGlyph","sourcePath":"components/status/StatusGlyph.jsx"},{"name":"WillBuildDot","sourcePath":"components/status/WillBuildDot.jsx"},{"name":"PROJECTS","sourcePath":"ui_kits/build-orchestrator/BuildData.jsx"},{"name":"DIRTY_ROOTS","sourcePath":"ui_kits/build-orchestrator/BuildData.jsx"},{"name":"GraphView","sourcePath":"ui_kits/build-orchestrator/GraphView.jsx"},{"name":"MainWindow","sourcePath":"ui_kits/build-orchestrator/MainWindow.jsx"}],"sourceHashes":{"components/controls/Button.jsx":"43592c7b17f4","components/controls/Chip.jsx":"0eb3b63db471","components/controls/IconButton.jsx":"97015346bebd","components/controls/Kbd.jsx":"9b1b2a9cb031","components/controls/Segment.jsx":"597c5a36e41b","components/data/Console.jsx":"5c73751873f9","components/data/Metric.jsx":"f390fa58566d","components/data/ProgressBar.jsx":"34132e44452a","components/data/ProjectRow.jsx":"f87bf12edcb9","components/data/Tag.jsx":"11f9347ec0c2","components/forms/Checkbox.jsx":"a3c034d22c37","components/forms/Field.jsx":"3346b105cca7","components/forms/Input.jsx":"fc8838d2e87e","components/forms/Select.jsx":"0eebe06553da","components/forms/Switch.jsx":"321935e2ba56","components/graph/DependencyGraphNode.jsx":"69952a1f9d0c","components/shell/Dialog.jsx":"e35e50d6de7e","components/shell/StatusBar.jsx":"a1c4d4bc8bd8","components/shell/Tabs.jsx":"2b0955a52d48","components/shell/TitleBar.jsx":"fab50616407e","components/shell/Toast.jsx":"5c21155bf6cf","components/shell/Toolbar.jsx":"a0930ca4b4af","components/shell/Tooltip.jsx":"ff7a674bc794","components/status/Spinner.jsx":"7d2a8799fa68","components/status/StatusBadge.jsx":"22c8307e3952","components/status/StatusGlyph.jsx":"bdf61b5349c9","components/status/WillBuildDot.jsx":"3f2c0d13f9f9","ui_kits/build-orchestrator/BuildData.jsx":"4a0afceb2057","ui_kits/build-orchestrator/GraphView.jsx":"1e9cd01c55bb","ui_kits/build-orchestrator/MainWindow.jsx":"cc55c919b88e"},"inlinedExternals":[],"unexposedExports":[{"name":"byName","sourcePath":"ui_kits/build-orchestrator/BuildData.jsx"},{"name":"computeWillBuild","sourcePath":"ui_kits/build-orchestrator/BuildData.jsx"},{"name":"dependents","sourcePath":"ui_kits/build-orchestrator/BuildData.jsx"},{"name":"downstreamOf","sourcePath":"ui_kits/build-orchestrator/BuildData.jsx"},{"name":"inputBase","sourcePath":"components/forms/Input.jsx"}]} */

(() => {

const __ds_ns = (window.DeltaBuildOrchestratorDS_eb0bd1 = window.DeltaBuildOrchestratorDS_eb0bd1 || {});

const __ds_scope = {};

(__ds_ns.__errors = __ds_ns.__errors || []);

// components/controls/Button.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const HEIGHTS = {
  sm: 24,
  md: 28,
  lg: 32
};
const VARIANTS = {
  primary: {
    base: {
      background: 'var(--amber)',
      color: 'var(--text-on-accent)',
      border: '1px solid transparent'
    },
    hover: {
      background: 'var(--amber-bright)'
    },
    active: {
      background: 'var(--amber-dim)'
    }
  },
  secondary: {
    base: {
      background: 'var(--surface-raised)',
      color: 'var(--text-primary)',
      border: '1px solid var(--border-strong)'
    },
    hover: {
      background: 'var(--surface-overlay)'
    },
    active: {
      background: 'var(--surface)'
    }
  },
  ghost: {
    base: {
      background: 'transparent',
      color: 'var(--text-secondary)',
      border: '1px solid transparent'
    },
    hover: {
      background: 'var(--surface-raised)',
      color: 'var(--text-primary)'
    },
    active: {
      background: 'var(--surface-overlay)'
    }
  },
  danger: {
    base: {
      background: 'var(--status-fail-soft)',
      color: 'var(--status-fail-text)',
      border: '1px solid var(--status-fail-border)'
    },
    hover: {
      background: 'rgba(238,90,82,0.18)'
    },
    active: {
      background: 'rgba(238,90,82,0.08)'
    }
  }
};
function Button({
  variant = 'secondary',
  size = 'md',
  icon,
  children,
  disabled,
  style,
  ...rest
}) {
  const [hover, setHover] = React.useState(false);
  const [press, setPress] = React.useState(false);
  const v = VARIANTS[variant] || VARIANTS.secondary;
  const stateStyle = disabled ? {} : press ? {
    ...v.hover,
    ...v.active
  } : hover ? v.hover : {};
  return /*#__PURE__*/React.createElement("button", _extends({
    type: "button",
    disabled: disabled,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => {
      setHover(false);
      setPress(false);
    },
    onMouseDown: () => setPress(true),
    onMouseUp: () => setPress(false),
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      gap: 6,
      height: HEIGHTS[size] || 28,
      padding: size === 'sm' ? '0 10px' : '0 12px',
      borderRadius: 'var(--radius-sm)',
      cursor: disabled ? 'default' : 'pointer',
      fontFamily: 'var(--font-sans)',
      fontSize: size === 'sm' ? 'var(--text-xs)' : 'var(--text-sm)',
      fontWeight: 500,
      lineHeight: 1,
      whiteSpace: 'nowrap',
      userSelect: 'none',
      opacity: disabled ? 0.45 : 1,
      transition: 'background var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard)',
      ...v.base,
      ...stateStyle,
      ...style
    }
  }, rest), icon, children);
}
Object.assign(__ds_scope, { Button });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/controls/Button.jsx", error: String((e && e.message) || e) }); }

// components/controls/Chip.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const Chevron = () => /*#__PURE__*/React.createElement("svg", {
  width: "10",
  height: "10",
  viewBox: "0 0 16 16",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "1.5",
  strokeLinecap: "round",
  strokeLinejoin: "round",
  style: {
    opacity: .7,
    flex: 'none'
  }
}, /*#__PURE__*/React.createElement("path", {
  d: "M4.5 6.5 8 10l3.5-3.5"
}));
function Chip({
  icon,
  label,
  value,
  chevron,
  active,
  onRemove,
  disabled,
  style,
  ...rest
}) {
  const [hover, setHover] = React.useState(false);
  const interactive = !!(rest.onClick || chevron);
  return /*#__PURE__*/React.createElement("button", _extends({
    type: "button",
    disabled: disabled,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 6,
      height: 24,
      padding: '0 8px',
      flex: 'none',
      background: active ? 'var(--amber-soft)' : hover && interactive && !disabled ? 'var(--surface-overlay)' : 'var(--surface-raised)',
      border: `1px solid ${active ? 'var(--amber-border)' : 'var(--border)'}`,
      borderRadius: 'var(--radius-xs)',
      color: active ? 'var(--amber-text)' : 'var(--text-secondary)',
      fontFamily: 'var(--font-sans)',
      fontSize: 'var(--text-xs)',
      fontWeight: 500,
      lineHeight: 1,
      cursor: interactive && !disabled ? 'pointer' : 'default',
      whiteSpace: 'nowrap',
      opacity: disabled ? 0.45 : 1,
      userSelect: 'none',
      transition: 'background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard)',
      ...style
    }
  }, rest), icon && /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      color: active ? 'var(--amber-text)' : 'var(--text-dim)'
    }
  }, icon), label, value != null && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontWeight: 400,
      color: active ? 'var(--amber-text)' : 'var(--text-primary)',
      fontVariantNumeric: 'tabular-nums'
    }
  }, value), chevron && /*#__PURE__*/React.createElement(Chevron, null), onRemove && /*#__PURE__*/React.createElement("span", {
    role: "button",
    "aria-label": "Kald\u0131r",
    onClick: e => {
      e.stopPropagation();
      onRemove();
    },
    style: {
      display: 'inline-flex',
      marginRight: -2,
      color: 'var(--text-dim)',
      cursor: 'pointer'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "10",
    height: "10",
    viewBox: "0 0 16 16",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "1.5",
    strokeLinecap: "round"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M4.5 4.5l7 7M11.5 4.5l-7 7"
  }))));
}
Object.assign(__ds_scope, { Chip });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/controls/Chip.jsx", error: String((e && e.message) || e) }); }

// components/controls/IconButton.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function IconButton({
  size = 'md',
  active,
  disabled,
  title,
  children,
  style,
  ...rest
}) {
  const [hover, setHover] = React.useState(false);
  const px = size === 'sm' ? 22 : size === 'lg' ? 30 : 26;
  return /*#__PURE__*/React.createElement("button", _extends({
    type: "button",
    disabled: disabled,
    "aria-label": title,
    title: title,
    "aria-pressed": active || undefined,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      width: px,
      height: px,
      padding: 0,
      flex: 'none',
      background: active ? 'var(--amber-soft)' : hover && !disabled ? 'var(--surface-raised)' : 'transparent',
      color: active ? 'var(--amber-text)' : hover && !disabled ? 'var(--text-primary)' : 'var(--text-secondary)',
      border: '1px solid transparent',
      borderRadius: 'var(--radius-sm)',
      cursor: disabled ? 'default' : 'pointer',
      opacity: disabled ? 0.45 : 1,
      transition: 'background var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard)',
      ...style
    }
  }, rest), children);
}
Object.assign(__ds_scope, { IconButton });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/controls/IconButton.jsx", error: String((e && e.message) || e) }); }

// components/controls/Kbd.jsx
try { (() => {
function Kbd({
  children,
  style
}) {
  return /*#__PURE__*/React.createElement("kbd", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      minWidth: 16,
      height: 18,
      padding: '0 5px',
      boxSizing: 'border-box',
      background: 'var(--surface-raised)',
      border: '1px solid var(--border-strong)',
      borderBottomWidth: 2,
      borderRadius: 'var(--radius-xs)',
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--text-2xs)',
      lineHeight: 1,
      color: 'var(--text-secondary)',
      whiteSpace: 'nowrap',
      ...style
    }
  }, children);
}
Object.assign(__ds_scope, { Kbd });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/controls/Kbd.jsx", error: String((e && e.message) || e) }); }

// components/controls/Segment.jsx
try { (() => {
function Segment({
  options = [],
  value,
  onChange,
  size = 'md',
  style
}) {
  const h = size === 'sm' ? 22 : 24;
  return /*#__PURE__*/React.createElement("div", {
    role: "radiogroup",
    style: {
      display: 'inline-flex',
      alignItems: 'stretch',
      height: h + 2,
      background: 'var(--surface-sunken)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-sm)',
      padding: 1,
      gap: 1,
      flex: 'none',
      boxSizing: 'border-box',
      ...style
    }
  }, options.map(opt => {
    const o = typeof opt === 'string' ? {
      value: opt,
      label: opt
    } : opt;
    const selected = o.value === value;
    return /*#__PURE__*/React.createElement("button", {
      key: o.value,
      type: "button",
      role: "radio",
      "aria-checked": selected,
      onClick: () => onChange && onChange(o.value),
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        padding: '0 10px',
        background: selected ? 'var(--surface-overlay)' : 'transparent',
        color: selected ? 'var(--text-primary)' : 'var(--text-dim)',
        border: 'none',
        borderRadius: 'var(--radius-xs)',
        fontFamily: 'var(--font-sans)',
        fontSize: 'var(--text-xs)',
        fontWeight: 500,
        lineHeight: 1,
        cursor: 'pointer',
        whiteSpace: 'nowrap',
        userSelect: 'none',
        transition: 'background var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard)'
      }
    }, o.label);
  }));
}
Object.assign(__ds_scope, { Segment });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/controls/Segment.jsx", error: String((e && e.message) || e) }); }

// components/data/Console.jsx
try { (() => {
const LINE_COLORS = {
  info: 'var(--text-secondary)',
  success: 'var(--status-success-text)',
  warn: 'var(--status-cycle-text)',
  error: 'var(--status-fail-text)',
  cmd: 'var(--text-primary)',
  dim: 'var(--text-faint)'
};
function ConsoleLine({
  type = 'info',
  time,
  children,
  style
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      gap: 10,
      alignItems: 'baseline',
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--text-xs)',
      lineHeight: 'var(--leading-mono)',
      color: LINE_COLORS[type] || LINE_COLORS.info,
      whiteSpace: 'pre-wrap',
      wordBreak: 'break-word',
      fontVariantNumeric: 'tabular-nums',
      ...style
    }
  }, time && /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--text-faint)',
      flex: 'none'
    }
  }, time), /*#__PURE__*/React.createElement("span", {
    style: {
      minWidth: 0
    }
  }, type === 'cmd' && /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--amber-text)',
      marginRight: 8
    }
  }, "\u25B8"), children));
}
function Console({
  lines,
  height,
  autoScroll = true,
  children,
  style
}) {
  const ref = React.useRef(null);
  React.useEffect(() => {
    if (autoScroll && ref.current) ref.current.scrollTop = ref.current.scrollHeight;
  });
  return /*#__PURE__*/React.createElement("div", {
    ref: ref,
    style: {
      background: 'var(--console-bg)',
      borderRadius: 'var(--radius-none)',
      borderTop: '1px solid var(--border-subtle)',
      padding: '8px 12px',
      overflowY: 'auto',
      boxSizing: 'border-box',
      height,
      ...style
    }
  }, lines ? lines.map((l, i) => /*#__PURE__*/React.createElement(ConsoleLine, {
    key: i,
    type: l.type,
    time: l.time
  }, l.text)) : children);
}
Object.assign(__ds_scope, { ConsoleLine, Console });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/Console.jsx", error: String((e && e.message) || e) }); }

// components/data/Metric.jsx
try { (() => {
function Metric({
  label,
  value,
  unit,
  tone = 'default',
  size = 'md',
  style
}) {
  const color = tone === 'success' ? 'var(--status-success-text)' : tone === 'fail' ? 'var(--status-fail-text)' : tone === 'amber' ? 'var(--amber-text)' : 'var(--text-primary)';
  return /*#__PURE__*/React.createElement("div", {
    style: {
      fontFamily: 'var(--font-sans)',
      minWidth: 0,
      ...style
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 'var(--text-2xs)',
      fontWeight: 500,
      letterSpacing: 'var(--tracking-caps)',
      textTransform: 'uppercase',
      color: 'var(--text-dim)',
      marginBottom: 2,
      whiteSpace: 'nowrap'
    }
  }, label), /*#__PURE__*/React.createElement("div", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontVariantNumeric: 'tabular-nums',
      fontSize: size === 'lg' ? 'var(--text-2xl)' : 'var(--text-lg)',
      fontWeight: size === 'lg' ? 400 : 500,
      lineHeight: 'var(--leading-tight)',
      color,
      whiteSpace: 'nowrap'
    }
  }, value, unit && /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: size === 'lg' ? 'var(--text-sm)' : 'var(--text-xs)',
      color: 'var(--text-dim)',
      marginLeft: 3
    }
  }, unit)));
}
Object.assign(__ds_scope, { Metric });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/Metric.jsx", error: String((e && e.message) || e) }); }

// components/data/ProgressBar.jsx
try { (() => {
if (typeof document !== 'undefined' && !document.getElementById('ds-progress-css')) {
  const s = document.createElement('style');
  s.id = 'ds-progress-css';
  s.textContent = `
@media (prefers-reduced-motion: no-preference) {
  .ds-progress-indet { animation: ds-progress-indet 1.4s var(--ease-in-out) infinite; }
}
@keyframes ds-progress-indet { 0% { transform: translateX(-110%); } 100% { transform: translateX(320%); } }`;
  document.head.appendChild(s);
}
const FILL = {
  building: 'var(--amber)',
  succeeded: 'var(--status-success)',
  failed: 'var(--status-fail)',
  neutral: 'var(--neutral-500)'
};
function ProgressBar({
  value,
  status = 'building',
  indeterminate,
  height = 4,
  style
}) {
  const fill = FILL[status] || FILL.building;
  return /*#__PURE__*/React.createElement("div", {
    role: "progressbar",
    "aria-valuenow": indeterminate ? undefined : Math.round(value || 0),
    "aria-valuemin": 0,
    "aria-valuemax": 100,
    style: {
      height,
      background: 'var(--surface-overlay)',
      borderRadius: 'var(--radius-xs)',
      overflow: 'hidden',
      position: 'relative',
      ...style
    }
  }, indeterminate ? /*#__PURE__*/React.createElement("div", {
    className: "ds-progress-indet",
    style: {
      position: 'absolute',
      inset: 0,
      width: '35%',
      background: fill,
      borderRadius: 'inherit'
    }
  }) : /*#__PURE__*/React.createElement("div", {
    style: {
      height: '100%',
      width: `${Math.max(0, Math.min(100, value || 0))}%`,
      background: fill,
      borderRadius: 'inherit',
      transition: 'width var(--duration-base) var(--ease-standard)'
    }
  }));
}
Object.assign(__ds_scope, { ProgressBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/ProgressBar.jsx", error: String((e && e.message) || e) }); }

// components/data/Tag.jsx
try { (() => {
function Tag({
  children,
  mono = true,
  tone = 'neutral',
  style
}) {
  const tones = {
    neutral: {
      color: 'var(--text-secondary)',
      border: 'var(--border)',
      bg: 'var(--surface-raised)'
    },
    dim: {
      color: 'var(--text-dim)',
      border: 'var(--border-subtle)',
      bg: 'transparent'
    },
    amber: {
      color: 'var(--amber-text)',
      border: 'var(--amber-border)',
      bg: 'var(--amber-soft)'
    }
  };
  const t = tones[tone] || tones.neutral;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      height: 18,
      padding: '0 6px',
      background: t.bg,
      border: `1px solid ${t.border}`,
      borderRadius: 'var(--radius-xs)',
      color: t.color,
      fontFamily: mono ? 'var(--font-mono)' : 'var(--font-sans)',
      fontSize: 'var(--text-2xs)',
      lineHeight: 1,
      whiteSpace: 'nowrap',
      fontVariantNumeric: 'tabular-nums',
      flex: 'none',
      ...style
    }
  }, children);
}
Object.assign(__ds_scope, { Tag });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/Tag.jsx", error: String((e && e.message) || e) }); }

// components/forms/Checkbox.jsx
try { (() => {
function Checkbox({
  checked,
  indeterminate,
  onChange,
  label,
  disabled,
  style
}) {
  return /*#__PURE__*/React.createElement("label", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 7,
      cursor: disabled ? 'default' : 'pointer',
      fontFamily: 'var(--font-sans)',
      fontSize: 'var(--text-sm)',
      color: 'var(--text-primary)',
      opacity: disabled ? 0.45 : 1,
      userSelect: 'none',
      ...style
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'relative',
      display: 'inline-flex',
      flex: 'none'
    }
  }, /*#__PURE__*/React.createElement("input", {
    type: "checkbox",
    checked: !!checked,
    disabled: disabled,
    onChange: e => onChange && onChange(e.target.checked),
    style: {
      position: 'absolute',
      inset: 0,
      opacity: 0,
      margin: 0,
      cursor: 'inherit'
    }
  }), /*#__PURE__*/React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: 14,
      height: 14,
      boxSizing: 'border-box',
      background: checked || indeterminate ? 'var(--amber)' : 'var(--surface-sunken)',
      border: `1px solid ${checked || indeterminate ? 'var(--amber)' : 'var(--border-strong)'}`,
      borderRadius: 'var(--radius-xs)',
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      transition: 'background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard)'
    }
  }, (checked || indeterminate) && /*#__PURE__*/React.createElement("svg", {
    width: "10",
    height: "10",
    viewBox: "0 0 16 16",
    fill: "none",
    stroke: "var(--text-on-accent)",
    strokeWidth: "2",
    strokeLinecap: "round",
    strokeLinejoin: "round"
  }, indeterminate ? /*#__PURE__*/React.createElement("path", {
    d: "M4.5 8h7"
  }) : /*#__PURE__*/React.createElement("path", {
    d: "M4 8.4l2.6 2.6L12 5.4"
  })))), label);
}
Object.assign(__ds_scope, { Checkbox });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Checkbox.jsx", error: String((e && e.message) || e) }); }

// components/forms/Field.jsx
try { (() => {
function Field({
  label,
  hint,
  error,
  htmlFor,
  inline,
  children,
  style
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: inline ? 'flex' : 'block',
      alignItems: inline ? 'center' : undefined,
      gap: inline ? 10 : undefined,
      ...style
    }
  }, label && /*#__PURE__*/React.createElement("label", {
    htmlFor: htmlFor,
    style: {
      display: 'block',
      marginBottom: inline ? 0 : 5,
      flex: inline ? 'none' : undefined,
      fontFamily: 'var(--font-sans)',
      fontSize: 'var(--text-xs)',
      fontWeight: 500,
      color: 'var(--text-secondary)',
      lineHeight: 1.2
    }
  }, label), children, (error || hint) && /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: inline ? 0 : 5,
      fontSize: 'var(--text-2xs)',
      lineHeight: 1.35,
      color: error ? 'var(--status-fail-text)' : 'var(--text-dim)'
    }
  }, error || hint));
}
Object.assign(__ds_scope, { Field });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Field.jsx", error: String((e && e.message) || e) }); }

// components/forms/Input.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
const inputBase = invalid => ({
  height: 28,
  padding: '0 8px',
  boxSizing: 'border-box',
  background: 'var(--surface-sunken)',
  border: `1px solid ${invalid ? 'var(--status-fail-border)' : 'var(--border-strong)'}`,
  borderRadius: 'var(--radius-sm)',
  color: 'var(--text-primary)',
  fontFamily: 'var(--font-sans)',
  fontSize: 'var(--text-sm)',
  lineHeight: 'normal',
  outline: 'none',
  width: '100%',
  transition: 'border-color var(--duration-fast) var(--ease-standard)'
});
function Input({
  mono,
  invalid,
  prefix,
  style,
  ...rest
}) {
  const [focus, setFocus] = React.useState(false);
  const field = /*#__PURE__*/React.createElement("input", _extends({}, rest, {
    onFocus: e => {
      setFocus(true);
      rest.onFocus && rest.onFocus(e);
    },
    onBlur: e => {
      setFocus(false);
      rest.onBlur && rest.onBlur(e);
    },
    style: {
      ...inputBase(invalid),
      fontFamily: mono ? 'var(--font-mono)' : 'var(--font-sans)',
      fontSize: mono ? 'var(--text-xs)' : 'var(--text-sm)',
      fontVariantNumeric: mono ? 'tabular-nums' : undefined,
      paddingLeft: prefix ? 26 : 8,
      boxShadow: focus ? `0 0 0 var(--focus-ring-width) var(--focus-ring)` : 'none',
      borderColor: focus && !invalid ? 'var(--amber-border)' : undefined,
      ...style
    }
  }));
  if (!prefix) return field;
  return /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'relative',
      display: 'inline-flex',
      width: style?.width || '100%'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'absolute',
      left: 8,
      top: '50%',
      transform: 'translateY(-50%)',
      display: 'inline-flex',
      color: 'var(--text-dim)',
      pointerEvents: 'none'
    }
  }, prefix), field);
}
Object.assign(__ds_scope, { inputBase, Input });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Input.jsx", error: String((e && e.message) || e) }); }

// components/forms/Select.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
function Select({
  invalid,
  children,
  style,
  ...rest
}) {
  const [focus, setFocus] = React.useState(false);
  return /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'relative',
      display: 'inline-flex',
      width: style?.width || '100%'
    }
  }, /*#__PURE__*/React.createElement("select", _extends({}, rest, {
    onFocus: e => {
      setFocus(true);
      rest.onFocus && rest.onFocus(e);
    },
    onBlur: e => {
      setFocus(false);
      rest.onBlur && rest.onBlur(e);
    },
    style: {
      ...__ds_scope.inputBase(invalid),
      appearance: 'none',
      WebkitAppearance: 'none',
      paddingRight: 26,
      cursor: 'pointer',
      boxShadow: focus ? `0 0 0 var(--focus-ring-width) var(--focus-ring)` : 'none',
      borderColor: focus && !invalid ? 'var(--amber-border)' : undefined,
      ...style
    }
  }), children), /*#__PURE__*/React.createElement("svg", {
    width: "12",
    height: "12",
    viewBox: "0 0 16 16",
    fill: "none",
    stroke: "var(--text-dim)",
    strokeWidth: "1.5",
    strokeLinecap: "round",
    strokeLinejoin: "round",
    style: {
      position: 'absolute',
      right: 8,
      top: '50%',
      transform: 'translateY(-50%)',
      pointerEvents: 'none'
    }
  }, /*#__PURE__*/React.createElement("path", {
    d: "M4.5 6.5 8 10l3.5-3.5"
  })));
}
Object.assign(__ds_scope, { Select });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Select.jsx", error: String((e && e.message) || e) }); }

// components/forms/Switch.jsx
try { (() => {
function Switch({
  checked,
  onChange,
  label,
  disabled,
  style
}) {
  return /*#__PURE__*/React.createElement("label", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 8,
      cursor: disabled ? 'default' : 'pointer',
      fontFamily: 'var(--font-sans)',
      fontSize: 'var(--text-sm)',
      color: 'var(--text-primary)',
      opacity: disabled ? 0.45 : 1,
      userSelect: 'none',
      ...style
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'relative',
      display: 'inline-flex',
      flex: 'none'
    }
  }, /*#__PURE__*/React.createElement("input", {
    type: "checkbox",
    role: "switch",
    checked: !!checked,
    disabled: disabled,
    onChange: e => onChange && onChange(e.target.checked),
    style: {
      position: 'absolute',
      inset: 0,
      opacity: 0,
      margin: 0,
      cursor: 'inherit'
    }
  }), /*#__PURE__*/React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: 28,
      height: 16,
      boxSizing: 'border-box',
      borderRadius: 'var(--radius-full)',
      background: checked ? 'var(--amber)' : 'var(--surface-overlay)',
      border: `1px solid ${checked ? 'var(--amber)' : 'var(--border-strong)'}`,
      display: 'inline-flex',
      alignItems: 'center',
      padding: 1,
      transition: 'background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      width: 12,
      height: 12,
      borderRadius: '50%',
      background: checked ? 'var(--text-on-accent)' : 'var(--neutral-300)',
      transform: checked ? 'translateX(12px)' : 'translateX(0)',
      transition: 'transform var(--duration-fast) var(--ease-standard), background var(--duration-fast) var(--ease-standard)'
    }
  }))), label);
}
Object.assign(__ds_scope, { Switch });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/forms/Switch.jsx", error: String((e && e.message) || e) }); }

// components/shell/Dialog.jsx
try { (() => {
if (typeof document !== 'undefined' && !document.getElementById('ds-dialog-css')) {
  const s = document.createElement('style');
  s.id = 'ds-dialog-css';
  s.textContent = `
@media (prefers-reduced-motion: no-preference) {
  .ds-dialog-in { animation: ds-dialog-in var(--duration-base) var(--ease-out); }
  .ds-scrim-in { animation: ds-scrim-in var(--duration-base) var(--ease-out); }
}
@keyframes ds-dialog-in { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }
@keyframes ds-scrim-in { from { opacity: 0; } to { opacity: 1; } }`;
  document.head.appendChild(s);
}
const XIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "13",
  height: "13",
  viewBox: "0 0 16 16",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "1.5",
  strokeLinecap: "round"
}, /*#__PURE__*/React.createElement("path", {
  d: "M4 4l8 8M12 4l-8 8"
}));
function Dialog({
  open,
  title,
  width = 440,
  onClose,
  footer,
  children,
  style
}) {
  if (!open) return null;
  return /*#__PURE__*/React.createElement("div", {
    className: "ds-scrim-in",
    onMouseDown: e => {
      if (e.target === e.currentTarget && onClose) onClose();
    },
    style: {
      position: 'absolute',
      inset: 0,
      zIndex: 100,
      background: 'var(--scrim)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center'
    }
  }, /*#__PURE__*/React.createElement("div", {
    role: "dialog",
    "aria-modal": "true",
    className: "ds-dialog-in",
    style: {
      width,
      maxWidth: 'calc(100% - 48px)',
      maxHeight: 'calc(100% - 48px)',
      display: 'flex',
      flexDirection: 'column',
      background: 'var(--surface-raised)',
      border: '1px solid var(--border-strong)',
      borderRadius: 'var(--radius-lg)',
      boxShadow: 'var(--elevation-overlay)',
      fontFamily: 'var(--font-sans)',
      ...style
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      padding: '10px 10px 10px 16px',
      borderBottom: '1px solid var(--border-subtle)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1,
      fontSize: 'var(--text-md)',
      fontWeight: 600,
      color: 'var(--text-primary)'
    }
  }, title), onClose && /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    size: "sm",
    title: "Kapat",
    onClick: onClose
  }, /*#__PURE__*/React.createElement(XIcon, null))), /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 16,
      overflowY: 'auto',
      fontSize: 'var(--text-sm)',
      color: 'var(--text-secondary)'
    }
  }, children), footer && /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      justifyContent: 'flex-end',
      gap: 8,
      padding: '12px 16px',
      borderTop: '1px solid var(--border-subtle)'
    }
  }, footer)));
}
Object.assign(__ds_scope, { Dialog });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/shell/Dialog.jsx", error: String((e && e.message) || e) }); }

// components/shell/StatusBar.jsx
try { (() => {
function StatusBar({
  children,
  right,
  style
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 14,
      height: 'var(--statusbar-height)',
      padding: '0 12px',
      boxSizing: 'border-box',
      background: 'var(--surface-base)',
      borderTop: '1px solid var(--border-subtle)',
      fontFamily: 'var(--font-sans)',
      fontSize: 'var(--text-2xs)',
      color: 'var(--text-dim)',
      flex: 'none',
      userSelect: 'none',
      whiteSpace: 'nowrap',
      ...style
    }
  }, children, /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  }), right);
}
function StatusBarItem({
  label,
  value,
  color,
  style
}) {
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 5,
      ...style
    }
  }, label && /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--text-faint)'
    }
  }, label), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontVariantNumeric: 'tabular-nums',
      color: color || 'var(--text-secondary)'
    }
  }, value));
}
Object.assign(__ds_scope, { StatusBar, StatusBarItem });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/shell/StatusBar.jsx", error: String((e && e.message) || e) }); }

// components/shell/Tabs.jsx
try { (() => {
function Tabs({
  tabs = [],
  active,
  onChange,
  style
}) {
  return /*#__PURE__*/React.createElement("div", {
    role: "tablist",
    style: {
      display: 'flex',
      alignItems: 'stretch',
      gap: 2,
      height: 34,
      borderBottom: '1px solid var(--border-subtle)',
      padding: '0 8px',
      fontFamily: 'var(--font-sans)',
      flex: 'none',
      boxSizing: 'border-box',
      ...style
    }
  }, tabs.map(t => {
    const sel = t.id === active;
    return /*#__PURE__*/React.createElement("button", {
      key: t.id,
      type: "button",
      role: "tab",
      "aria-selected": sel,
      onClick: () => onChange && onChange(t.id),
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '0 10px',
        background: 'transparent',
        border: 'none',
        cursor: 'pointer',
        borderBottom: `2px solid ${sel ? 'var(--amber)' : 'transparent'}`,
        marginBottom: -1,
        color: sel ? 'var(--text-primary)' : 'var(--text-dim)',
        fontSize: 'var(--text-sm)',
        fontWeight: 500,
        whiteSpace: 'nowrap',
        userSelect: 'none',
        transition: 'color var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard)'
      }
    }, t.label, t.count != null && /*#__PURE__*/React.createElement("span", {
      style: {
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--text-2xs)',
        fontVariantNumeric: 'tabular-nums',
        color: sel ? 'var(--text-secondary)' : 'var(--text-faint)'
      }
    }, t.count));
  }));
}
Object.assign(__ds_scope, { Tabs });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/shell/Tabs.jsx", error: String((e && e.message) || e) }); }

// components/shell/TitleBar.jsx
try { (() => {
const WinBtn = ({
  kind,
  onClick
}) => {
  const [hover, setHover] = React.useState(false);
  const danger = kind === 'close';
  return /*#__PURE__*/React.createElement("button", {
    type: "button",
    "aria-label": kind === 'close' ? 'Kapat' : kind === 'max' ? 'Ekranı kapla' : 'Simge durumuna küçült',
    onClick: onClick,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      width: 46,
      height: '100%',
      border: 'none',
      padding: 0,
      cursor: 'pointer',
      background: hover ? danger ? 'var(--status-fail)' : 'var(--surface-raised)' : 'transparent',
      color: hover && danger ? '#fff' : 'var(--text-secondary)',
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      transition: 'background var(--duration-fast) var(--ease-standard)'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "10",
    height: "10",
    viewBox: "0 0 10 10",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "1"
  }, kind === 'min' && /*#__PURE__*/React.createElement("path", {
    d: "M0.5 5h9"
  }), kind === 'max' && /*#__PURE__*/React.createElement("rect", {
    x: "1",
    y: "1",
    width: "8",
    height: "8"
  }), kind === 'close' && /*#__PURE__*/React.createElement("path", {
    d: "M1 1l8 8M9 1l-8 8"
  })));
};
function TitleBar({
  title = 'Build Orchestrator',
  logoSrc,
  children,
  onMinimize,
  onMaximize,
  onClose,
  style
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      height: 'var(--titlebar-height)',
      background: 'var(--surface-base)',
      borderBottom: '1px solid var(--border-subtle)',
      paddingLeft: 12,
      flex: 'none',
      userSelect: 'none',
      fontFamily: 'var(--font-sans)',
      ...style
    }
  }, logoSrc && /*#__PURE__*/React.createElement("img", {
    src: logoSrc,
    alt: "Delta",
    style: {
      height: 15,
      display: 'block',
      marginRight: 10
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 'var(--text-xs)',
      color: 'var(--text-dim)',
      whiteSpace: 'nowrap'
    }
  }, title), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      minWidth: 0
    }
  }, children), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignSelf: 'stretch'
    }
  }, /*#__PURE__*/React.createElement(WinBtn, {
    kind: "min",
    onClick: onMinimize
  }), /*#__PURE__*/React.createElement(WinBtn, {
    kind: "max",
    onClick: onMaximize
  }), /*#__PURE__*/React.createElement(WinBtn, {
    kind: "close",
    onClick: onClose
  })));
}
Object.assign(__ds_scope, { TitleBar });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/shell/TitleBar.jsx", error: String((e && e.message) || e) }); }

// components/shell/Toolbar.jsx
try { (() => {
function Toolbar({
  children,
  style
}) {
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      height: 'var(--toolbar-height)',
      padding: '0 12px',
      boxSizing: 'border-box',
      background: 'var(--surface)',
      borderBottom: '1px solid var(--border-subtle)',
      flex: 'none',
      fontFamily: 'var(--font-sans)',
      ...style
    }
  }, children);
}
function ToolbarSep({
  style
}) {
  return /*#__PURE__*/React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: 1,
      height: 20,
      background: 'var(--border)',
      flex: 'none',
      margin: '0 4px',
      ...style
    }
  });
}
function ToolbarSpacer() {
  return /*#__PURE__*/React.createElement("span", {
    style: {
      flex: 1
    }
  });
}
Object.assign(__ds_scope, { Toolbar, ToolbarSep, ToolbarSpacer });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/shell/Toolbar.jsx", error: String((e && e.message) || e) }); }

// components/shell/Tooltip.jsx
try { (() => {
function Tooltip({
  content,
  kbd,
  side = 'top',
  children,
  style
}) {
  const [open, setOpen] = React.useState(false);
  const posStyle = side === 'bottom' ? {
    top: 'calc(100% + 6px)',
    left: '50%',
    transform: 'translateX(-50%)'
  } : side === 'right' ? {
    left: 'calc(100% + 6px)',
    top: '50%',
    transform: 'translateY(-50%)'
  } : side === 'left' ? {
    right: 'calc(100% + 6px)',
    top: '50%',
    transform: 'translateY(-50%)'
  } : {
    bottom: 'calc(100% + 6px)',
    left: '50%',
    transform: 'translateX(-50%)'
  };
  return /*#__PURE__*/React.createElement("span", {
    style: {
      position: 'relative',
      display: 'inline-flex',
      ...style
    },
    onMouseEnter: () => setOpen(true),
    onMouseLeave: () => setOpen(false),
    onFocus: () => setOpen(true),
    onBlur: () => setOpen(false)
  }, children, open && /*#__PURE__*/React.createElement("span", {
    role: "tooltip",
    style: {
      position: 'absolute',
      zIndex: 50,
      ...posStyle,
      display: 'inline-flex',
      alignItems: 'center',
      gap: 8,
      background: 'var(--surface-overlay)',
      border: '1px solid var(--border-strong)',
      borderRadius: 'var(--radius-sm)',
      boxShadow: 'var(--elevation-popover)',
      padding: '4px 8px',
      whiteSpace: 'nowrap',
      fontFamily: 'var(--font-sans)',
      fontSize: 'var(--text-xs)',
      color: 'var(--text-primary)'
    }
  }, content, kbd && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--text-2xs)',
      color: 'var(--text-dim)'
    }
  }, kbd)));
}
Object.assign(__ds_scope, { Tooltip });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/shell/Tooltip.jsx", error: String((e && e.message) || e) }); }

// components/status/Spinner.jsx
try { (() => {
if (typeof document !== 'undefined' && !document.getElementById('ds-spinner-css')) {
  const s = document.createElement('style');
  s.id = 'ds-spinner-css';
  s.textContent = `
@media (prefers-reduced-motion: no-preference) {
  .ds-spinner-rot { animation: ds-spinner-rot 900ms linear infinite; transform-origin: center; transform-box: fill-box; }
}
@keyframes ds-spinner-rot { to { transform: rotate(360deg); } }`;
  document.head.appendChild(s);
}

/* Genel aktivite spinner'ı — 270° arc, currentColor. Sakin, sabit hız. */
function Spinner({
  size = 14,
  color,
  title = 'Yükleniyor',
  style
}) {
  return /*#__PURE__*/React.createElement("span", {
    role: "img",
    "aria-label": title,
    style: {
      display: 'inline-flex',
      color: color || 'var(--text-secondary)',
      flex: 'none',
      ...style
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: size,
    height: size,
    viewBox: "0 0 16 16",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "1.5",
    strokeLinecap: "round"
  }, /*#__PURE__*/React.createElement("g", {
    className: "ds-spinner-rot"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M8 1.3a6.7 6.7 0 1 1-6.7 6.7"
  }))));
}
Object.assign(__ds_scope, { Spinner });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/status/Spinner.jsx", error: String((e && e.message) || e) }); }

// components/status/StatusGlyph.jsx
try { (() => {
function _extends() { return _extends = Object.assign ? Object.assign.bind() : function (n) { for (var e = 1; e < arguments.length; e++) { var t = arguments[e]; for (var r in t) ({}).hasOwnProperty.call(t, r) && (n[r] = t[r]); } return n; }, _extends.apply(null, arguments); }
/* Statü meta — renk + glyph + metin ÜÇÜ BİRDEN kullanılır (colorblind-safe). */
const STATUS_META = {
  discovered: {
    color: 'var(--text-faint)',
    label: 'Keşfedildi'
  },
  queued: {
    color: 'var(--status-queued-text)',
    label: 'Sırada'
  },
  building: {
    color: 'var(--amber-text)',
    label: 'Derleniyor'
  },
  succeeded: {
    color: 'var(--status-success-text)',
    label: 'Başarılı'
  },
  failed: {
    color: 'var(--status-fail-text)',
    label: 'Başarısız'
  },
  skipped: {
    color: 'var(--status-skipped-text)',
    label: 'Atlandı'
  },
  cycle: {
    color: 'var(--status-cycle-text)',
    label: 'Döngü'
  }
};

/* Keyframes (transform+opacity dışına çıkmaz; reduced-motion'da kapalı) */
if (typeof document !== 'undefined' && !document.getElementById('ds-status-glyph-css')) {
  const s = document.createElement('style');
  s.id = 'ds-status-glyph-css';
  s.textContent = `
@media (prefers-reduced-motion: no-preference) {
  .ds-spin { animation: ds-spin 900ms linear infinite; transform-origin: center; transform-box: fill-box; }
  .ds-pulse { animation: ds-pulse 1.6s var(--ease-in-out) infinite; }
}
@keyframes ds-spin { to { transform: rotate(360deg); } }
@keyframes ds-pulse { 0%,100% { opacity: 1; } 50% { opacity: .45; } }`;
  document.head.appendChild(s);
}
const RING = {
  cx: 8,
  cy: 8,
  r: 6.7,
  fill: 'none',
  strokeWidth: 1,
  opacity: 0.6
};

/* İç glyph'ler: Lucide geometrisinden uyarlanmış tutarlı 1.5px stroke (emoji değil). */
function inner(status) {
  switch (status) {
    case 'succeeded':
      return /*#__PURE__*/React.createElement("path", {
        d: "M5.1 8.4l2 2 3.8-4.6"
      });
    case 'failed':
      return /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("path", {
        d: "M5.7 5.7l4.6 4.6"
      }), /*#__PURE__*/React.createElement("path", {
        d: "M10.3 5.7l-4.6 4.6"
      }));
    case 'skipped':
      return /*#__PURE__*/React.createElement("path", {
        d: "M5.1 8h5.8"
      });
    case 'queued':
      return /*#__PURE__*/React.createElement("path", {
        d: "M8 4.8v3.4l2.1 1.3"
      });
    default:
      return null;
  }
}
function StatusGlyph({
  status = 'discovered',
  size = 16,
  title,
  style
}) {
  const meta = STATUS_META[status] || STATUS_META.discovered;
  const common = {
    width: size,
    height: size,
    viewBox: '0 0 16 16',
    fill: 'none',
    stroke: 'currentColor',
    strokeWidth: 1.5,
    strokeLinecap: 'round',
    strokeLinejoin: 'round',
    style: {
      display: 'block'
    }
  };
  let body;
  if (status === 'building') {
    body = /*#__PURE__*/React.createElement("g", {
      className: "ds-spin"
    }, /*#__PURE__*/React.createElement("path", {
      d: "M8 1.3a6.7 6.7 0 1 1-6.7 6.7"
    }));
  } else if (status === 'cycle') {
    body = /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("path", {
      d: "M8 2.7 14.4 13.2H1.6Z"
    }), /*#__PURE__*/React.createElement("path", {
      d: "M8 6.6v3"
    }), /*#__PURE__*/React.createElement("path", {
      d: "M8 11.4h.01"
    }));
  } else if (status === 'discovered') {
    body = /*#__PURE__*/React.createElement("circle", _extends({}, RING, {
      strokeDasharray: "2.3 2.5",
      opacity: 0.9
    }));
  } else {
    body = /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement("circle", RING), inner(status));
  }
  return /*#__PURE__*/React.createElement("span", {
    role: "img",
    "aria-label": title || meta.label,
    title: title,
    className: status === 'building' ? 'ds-pulse' : undefined,
    style: {
      display: 'inline-flex',
      color: meta.color,
      flex: 'none',
      ...style
    }
  }, /*#__PURE__*/React.createElement("svg", common, body));
}
Object.assign(__ds_scope, { STATUS_META, StatusGlyph });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/status/StatusGlyph.jsx", error: String((e && e.message) || e) }); }

// components/graph/DependencyGraphNode.jsx
try { (() => {
if (typeof document !== 'undefined' && !document.getElementById('ds-graphnode-css')) {
  const s = document.createElement('style');
  s.id = 'ds-graphnode-css';
  s.textContent = `
@media (prefers-reduced-motion: no-preference) {
  .ds-node-pulse { animation: ds-node-pulse 1.6s var(--ease-in-out) infinite; }
}
@keyframes ds-node-pulse { 0%,100% { opacity: 1; } 50% { opacity: .5; } }`;
  document.head.appendChild(s);
}
const NODE = {
  discovered: {
    border: 'var(--border-strong)',
    bg: 'var(--surface-raised)',
    dash: true
  },
  queued: {
    border: 'var(--status-queued)',
    bg: 'var(--surface-raised)'
  },
  building: {
    border: 'var(--amber)',
    bg: 'var(--amber-soft)',
    pulse: true
  },
  succeeded: {
    border: 'var(--status-success)',
    bg: 'var(--status-success-soft)'
  },
  failed: {
    border: 'var(--status-fail)',
    bg: 'var(--status-fail-soft)'
  },
  skipped: {
    border: 'var(--status-skipped-border)',
    bg: 'var(--status-skipped-soft)'
  },
  cycle: {
    border: 'var(--status-cycle)',
    bg: 'var(--status-cycle-soft)'
  }
};
function DependencyGraphNode({
  label,
  status = 'discovered',
  selected,
  size = 30,
  showLabel = true,
  onClick,
  style
}) {
  const n = NODE[status] || NODE.discovered;
  const meta = __ds_scope.STATUS_META[status] || __ds_scope.STATUS_META.discovered;
  return /*#__PURE__*/React.createElement("div", {
    role: onClick ? 'button' : undefined,
    tabIndex: onClick ? 0 : undefined,
    onClick: onClick,
    onKeyDown: onClick ? e => {
      if (e.key === 'Enter') onClick(e);
    } : undefined,
    title: `${label} — ${meta.label}`,
    style: {
      display: 'inline-flex',
      flexDirection: 'column',
      alignItems: 'center',
      gap: 5,
      cursor: onClick ? 'pointer' : 'default',
      userSelect: 'none',
      ...style
    }
  }, /*#__PURE__*/React.createElement("span", {
    className: n.pulse ? 'ds-node-pulse' : undefined,
    style: {
      width: size,
      height: size,
      boxSizing: 'border-box',
      background: n.bg,
      border: `${selected ? 2 : 1.5}px ${n.dash ? 'dashed' : 'solid'} ${n.border}`,
      borderRadius: 'var(--radius-sm)',
      outline: selected ? '2px solid var(--focus-ring)' : 'none',
      outlineOffset: 2,
      display: 'inline-flex',
      alignItems: 'center',
      justifyContent: 'center',
      transition: 'border-color var(--duration-fast) var(--ease-standard), background var(--duration-fast) var(--ease-standard)'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: size * 0.5,
    height: size * 0.5,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: meta.color,
    strokeWidth: "1.6",
    strokeLinecap: "round",
    strokeLinejoin: "round"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"
  }), /*#__PURE__*/React.createElement("path", {
    d: "m3.3 7 8.7 5 8.7-5"
  }), /*#__PURE__*/React.createElement("path", {
    d: "M12 22V12"
  }))), showLabel && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontSize: 10,
      lineHeight: 1.2,
      color: selected ? 'var(--text-primary)' : 'var(--text-dim)',
      maxWidth: size * 3.4,
      overflow: 'hidden',
      textOverflow: 'ellipsis',
      whiteSpace: 'nowrap'
    }
  }, label));
}
Object.assign(__ds_scope, { DependencyGraphNode });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/graph/DependencyGraphNode.jsx", error: String((e && e.message) || e) }); }

// components/shell/Toast.jsx
try { (() => {
const TONE_TO_STATUS = {
  success: 'succeeded',
  fail: 'failed',
  warn: 'cycle',
  info: null,
  building: 'building'
};
function Toast({
  tone = 'info',
  title,
  description,
  action,
  onClose,
  style
}) {
  const status = TONE_TO_STATUS[tone];
  return /*#__PURE__*/React.createElement("div", {
    role: "status",
    style: {
      display: 'flex',
      alignItems: 'flex-start',
      gap: 10,
      width: 340,
      boxSizing: 'border-box',
      padding: '10px 12px',
      background: 'var(--surface-overlay)',
      border: '1px solid var(--border-strong)',
      borderRadius: 'var(--radius-lg)',
      boxShadow: 'var(--elevation-overlay)',
      fontFamily: 'var(--font-sans)',
      ...style
    }
  }, status && /*#__PURE__*/React.createElement(__ds_scope.StatusGlyph, {
    status: status,
    size: 15,
    style: {
      marginTop: 1
    }
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minWidth: 0
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 'var(--text-sm)',
      fontWeight: 500,
      color: 'var(--text-primary)',
      lineHeight: 'var(--leading-snug)'
    }
  }, title), description && /*#__PURE__*/React.createElement("div", {
    style: {
      fontSize: 'var(--text-xs)',
      color: 'var(--text-secondary)',
      marginTop: 2,
      lineHeight: 'var(--leading-snug)'
    }
  }, description), action && /*#__PURE__*/React.createElement("div", {
    style: {
      marginTop: 8,
      display: 'flex',
      gap: 8
    }
  }, action)), onClose && /*#__PURE__*/React.createElement("button", {
    type: "button",
    "aria-label": "Kapat",
    onClick: onClose,
    style: {
      background: 'none',
      border: 'none',
      padding: 2,
      cursor: 'pointer',
      color: 'var(--text-dim)',
      display: 'inline-flex',
      flex: 'none'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "12",
    height: "12",
    viewBox: "0 0 16 16",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: "1.5",
    strokeLinecap: "round"
  }, /*#__PURE__*/React.createElement("path", {
    d: "M4 4l8 8M12 4l-8 8"
  }))));
}
Object.assign(__ds_scope, { Toast });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/shell/Toast.jsx", error: String((e && e.message) || e) }); }

// components/status/StatusBadge.jsx
try { (() => {
const TONE = {
  discovered: {
    bg: 'transparent',
    border: 'var(--border)'
  },
  queued: {
    bg: 'var(--status-queued-soft)',
    border: 'var(--border)'
  },
  building: {
    bg: 'var(--status-building-soft)',
    border: 'var(--status-building-border)'
  },
  succeeded: {
    bg: 'var(--status-success-soft)',
    border: 'var(--status-success-border)'
  },
  failed: {
    bg: 'var(--status-fail-soft)',
    border: 'var(--status-fail-border)'
  },
  skipped: {
    bg: 'var(--status-skipped-soft)',
    border: 'var(--status-skipped-border)'
  },
  cycle: {
    bg: 'var(--status-cycle-soft)',
    border: 'var(--status-cycle-border)'
  }
};
function StatusBadge({
  status = 'discovered',
  label,
  size = 'md',
  detail,
  style
}) {
  const meta = __ds_scope.STATUS_META[status] || __ds_scope.STATUS_META.discovered;
  const tone = TONE[status] || TONE.discovered;
  const sm = size === 'sm';
  return /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: sm ? 4 : 5,
      height: sm ? 18 : 20,
      padding: sm ? '0 6px 0 4px' : '0 8px 0 6px',
      background: tone.bg,
      border: `1px solid ${tone.border}`,
      borderRadius: 'var(--radius-xs)',
      color: meta.color,
      fontFamily: 'var(--font-sans)',
      fontSize: sm ? 'var(--text-2xs)' : 'var(--text-xs)',
      fontWeight: 500,
      lineHeight: 1,
      whiteSpace: 'nowrap',
      flex: 'none',
      ...style
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.StatusGlyph, {
    status: status,
    size: sm ? 11 : 13
  }), label || meta.label, detail != null && /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontWeight: 400,
      color: meta.color,
      opacity: .85,
      fontVariantNumeric: 'tabular-nums'
    }
  }, detail));
}
Object.assign(__ds_scope, { StatusBadge });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/status/StatusBadge.jsx", error: String((e && e.message) || e) }); }

// components/status/WillBuildDot.jsx
try { (() => {
const STATES = {
  dirty: {
    title: 'Değişti — derlenecek'
  },
  clean: {
    title: 'Güncel — atlanacak'
  },
  unknown: {
    title: 'Bilinmiyor — Sync bekleniyor'
  }
};

/* Will-build noktası: statü accent'inden AYRI, ortogonal semantik. */
function WillBuildDot({
  state = 'unknown',
  size,
  title,
  style
}) {
  const s = size || 'var(--dot-size)';
  const base = {
    display: 'inline-block',
    width: s,
    height: s,
    borderRadius: '50%',
    flex: 'none',
    boxSizing: 'border-box',
    ...style
  };
  const look = state === 'dirty' ? {
    background: 'var(--dot-dirty)'
  } : state === 'clean' ? {
    background: 'var(--dot-clean)'
  } : {
    background: 'var(--dot-unknown)',
    border: 'var(--dot-outline-width) solid var(--text-faint)'
  };
  return /*#__PURE__*/React.createElement("span", {
    role: "img",
    "aria-label": title || STATES[state].title,
    title: title || STATES[state].title,
    style: {
      ...base,
      ...look
    }
  });
}
Object.assign(__ds_scope, { WillBuildDot });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/status/WillBuildDot.jsx", error: String((e && e.message) || e) }); }

// components/data/ProjectRow.jsx
try { (() => {
const STRIP = {
  discovered: 'transparent',
  queued: 'var(--status-queued)',
  building: 'var(--status-building)',
  succeeded: 'var(--status-success)',
  failed: 'var(--status-fail)',
  skipped: 'var(--status-skipped)',
  cycle: 'var(--status-cycle)'
};
const BoxGlyph = () => /*#__PURE__*/React.createElement("svg", {
  width: "14",
  height: "14",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "1.6",
  strokeLinecap: "round",
  strokeLinejoin: "round",
  style: {
    flex: 'none'
  }
}, /*#__PURE__*/React.createElement("path", {
  d: "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"
}), /*#__PURE__*/React.createElement("path", {
  d: "m3.3 7 8.7 5 8.7-5"
}), /*#__PURE__*/React.createElement("path", {
  d: "M12 22V12"
}));
const FolderIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "13",
  height: "13",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "1.8",
  strokeLinecap: "round",
  strokeLinejoin: "round"
}, /*#__PURE__*/React.createElement("path", {
  d: "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"
}));
const CodeIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "13",
  height: "13",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "1.8",
  strokeLinecap: "round",
  strokeLinejoin: "round"
}, /*#__PURE__*/React.createElement("polyline", {
  points: "16 18 22 12 16 6"
}), /*#__PURE__*/React.createElement("polyline", {
  points: "8 6 2 12 8 18"
}));
function ProjectRow({
  name,
  solution,
  status = 'discovered',
  willBuild = 'unknown',
  duration,
  selected,
  compact,
  onOpenFile,
  onOpenVS,
  onClick,
  style
}) {
  const [hover, setHover] = React.useState(false);
  const meta = __ds_scope.STATUS_META[status] || __ds_scope.STATUS_META.discovered;
  const dim = status === 'skipped' || status === 'discovered';
  return /*#__PURE__*/React.createElement("div", {
    role: "row",
    tabIndex: onClick ? 0 : undefined,
    onClick: onClick,
    onKeyDown: onClick ? e => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        onClick(e);
      }
    } : undefined,
    onMouseEnter: () => setHover(true),
    onMouseLeave: () => setHover(false),
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      height: compact ? 'var(--row-height-compact)' : 'var(--row-height)',
      padding: '0 10px 0 0',
      position: 'relative',
      boxSizing: 'border-box',
      background: selected ? 'var(--surface-raised)' : hover ? 'var(--surface-hover)' : 'transparent',
      borderBottom: '1px solid var(--border-subtle)',
      cursor: onClick ? 'pointer' : 'default',
      userSelect: 'none',
      transition: 'background var(--duration-fast) var(--ease-standard)',
      fontFamily: 'var(--font-sans)',
      ...style
    }
  }, /*#__PURE__*/React.createElement("span", {
    "aria-hidden": "true",
    style: {
      width: 2,
      alignSelf: 'stretch',
      background: STRIP[status] || 'transparent',
      flex: 'none'
    }
  }), /*#__PURE__*/React.createElement(__ds_scope.WillBuildDot, {
    state: willBuild,
    style: {
      marginLeft: 6
    }
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      color: dim ? 'var(--text-faint)' : 'var(--text-dim)'
    }
  }, /*#__PURE__*/React.createElement(BoxGlyph, null)), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      alignItems: 'baseline',
      gap: 8,
      minWidth: 0,
      flex: 1
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 'var(--text-sm)',
      fontWeight: 500,
      color: dim ? 'var(--text-dim)' : 'var(--text-primary)',
      whiteSpace: 'nowrap',
      overflow: 'hidden',
      textOverflow: 'ellipsis'
    }
  }, name), solution && !compact && /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 'var(--text-xs)',
      color: 'var(--text-faint)',
      whiteSpace: 'nowrap',
      overflow: 'hidden',
      textOverflow: 'ellipsis'
    }
  }, solution)), (onOpenFile || onOpenVS) && /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'flex',
      gap: 2,
      opacity: hover ? 1 : 0,
      transition: 'opacity var(--duration-fast) var(--ease-standard)'
    }
  }, onOpenFile && /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    size: "sm",
    title: "Dosyada A\xE7",
    onClick: e => {
      e.stopPropagation();
      onOpenFile();
    }
  }, /*#__PURE__*/React.createElement(FolderIcon, null)), onOpenVS && /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    size: "sm",
    title: "VS'de A\xE7",
    onClick: e => {
      e.stopPropagation();
      onOpenVS();
    }
  }, /*#__PURE__*/React.createElement(CodeIcon, null))), /*#__PURE__*/React.createElement(__ds_scope.StatusGlyph, {
    status: status,
    size: 14
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--text-xs)',
      fontVariantNumeric: 'tabular-nums',
      color: status === 'failed' ? 'var(--status-fail-text)' : 'var(--text-dim)',
      minWidth: 52,
      textAlign: 'right',
      whiteSpace: 'nowrap'
    }
  }, duration || '—'));
}
Object.assign(__ds_scope, { ProjectRow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "components/data/ProjectRow.jsx", error: String((e && e.message) || e) }); }

// ui_kits/build-orchestrator/BuildData.jsx
try { (() => {
// OSYS örnek proje verisi + bağımlılık yardımcıları (UI kit simülasyonu için)
const PROJECTS = [{
  name: 'Osys.Core',
  solution: 'Osys.sln',
  deps: [],
  dur: 2100,
  pos: {
    x: 30,
    y: 150
  }
}, {
  name: 'Osys.Data',
  solution: 'Osys.sln',
  deps: ['Osys.Core'],
  dur: 2600,
  pos: {
    x: 220,
    y: 60
  }
}, {
  name: 'Osys.Ortak.UI',
  solution: 'Osys.sln',
  deps: ['Osys.Core'],
  dur: 1900,
  pos: {
    x: 220,
    y: 240
  }
}, {
  name: 'Osys.Entegrasyon',
  solution: 'Osys.sln',
  deps: ['Osys.Core'],
  dur: 1400,
  pos: {
    x: 220,
    y: 400
  }
}, {
  name: 'Osys.Arac.Satis',
  solution: 'Osys.Satis.sln',
  deps: ['Osys.Data', 'Osys.Ortak.UI'],
  dur: 2900,
  pos: {
    x: 470,
    y: 20
  }
}, {
  name: 'Osys.IkinciEl',
  solution: 'Osys.Satis.sln',
  deps: ['Osys.Data', 'Osys.Ortak.UI'],
  dur: 2300,
  pos: {
    x: 470,
    y: 130
  }
}, {
  name: 'Osys.Servis.Core',
  solution: 'Osys.Servis.sln',
  deps: ['Osys.Data'],
  dur: 2400,
  pos: {
    x: 470,
    y: 250
  }
}, {
  name: 'Osys.Parca.Core',
  solution: 'Osys.Parca.sln',
  deps: ['Osys.Data'],
  dur: 1700,
  pos: {
    x: 470,
    y: 370
  }
}, {
  name: 'Osys.Rapor',
  solution: 'Osys.sln',
  deps: ['Osys.Data'],
  dur: 2000,
  pos: {
    x: 470,
    y: 480
  }
}, {
  name: 'Osys.Muhasebe',
  solution: 'Osys.sln',
  deps: ['Osys.Data', 'Osys.Entegrasyon'],
  dur: 2200,
  pos: {
    x: 700,
    y: 440
  }
}, {
  name: 'Osys.Servis.Randevu',
  solution: 'Osys.Servis.sln',
  deps: ['Osys.Servis.Core'],
  dur: 1600,
  pos: {
    x: 700,
    y: 250
  }
}, {
  name: 'Osys.Parca.Api',
  solution: 'Osys.Parca.sln',
  deps: ['Osys.Parca.Core'],
  dur: 2500,
  pos: {
    x: 700,
    y: 350
  },
  fails: true
}, {
  name: 'Osys.Bildirim',
  solution: 'Osys.sln',
  deps: ['Osys.Core'],
  dur: 1200,
  pos: {
    x: 700,
    y: 120
  }
}, {
  name: 'Osys.Mobil.Client',
  solution: 'Osys.Mobil.sln',
  deps: ['Osys.Parca.Api', 'Osys.Servis.Core'],
  dur: 3100,
  pos: {
    x: 930,
    y: 300
  }
}];

/* Sync'te "değişmiş" bulunan projeler */
const DIRTY_ROOTS = ['Osys.Servis.Core', 'Osys.Parca.Core'];
const byName = Object.fromEntries(PROJECTS.map(p => [p.name, p]));

/* name -> onu doğrudan kullanan projeler */
const dependents = {};
PROJECTS.forEach(p => p.deps.forEach(d => {
  (dependents[d] = dependents[d] || []).push(p.name);
}));

/* Dirty kökler + tüm aşağı-akış bağımlıları = derlenecekler */
function computeWillBuild() {
  const dirty = new Set(DIRTY_ROOTS);
  let grew = true;
  while (grew) {
    grew = false;
    PROJECTS.forEach(p => {
      if (!dirty.has(p.name) && p.deps.some(d => dirty.has(d))) {
        dirty.add(p.name);
        grew = true;
      }
    });
  }
  const map = {};
  PROJECTS.forEach(p => {
    map[p.name] = dirty.has(p.name) ? 'dirty' : 'clean';
  });
  return map;
}

/* Bir kökten ulaşılabilen tüm aşağı-akış projeleri (kendisi hariç) */
function downstreamOf(root) {
  const out = new Set();
  const walk = n => (dependents[n] || []).forEach(m => {
    if (!out.has(m)) {
      out.add(m);
      walk(m);
    }
  });
  walk(root);
  return out;
}
Object.assign(__ds_scope, { PROJECTS, DIRTY_ROOTS, byName, dependents, computeWillBuild, downstreamOf });
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/build-orchestrator/BuildData.jsx", error: String((e && e.message) || e) }); }

// ui_kits/build-orchestrator/GraphView.jsx
try { (() => {
const NODE = 34; // DependencyGraphNode size

function GraphView({
  statuses,
  selected,
  onSelect
}) {
  const center = p => ({
    x: p.pos.x + NODE / 2,
    y: p.pos.y + NODE / 2
  });
  return /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflow: 'auto',
      background: 'var(--surface-base)',
      position: 'relative'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'relative',
      width: 1010,
      height: 560,
      margin: '12px auto'
    }
  }, /*#__PURE__*/React.createElement("svg", {
    width: "1010",
    height: "560",
    style: {
      position: 'absolute',
      inset: 0
    },
    "aria-hidden": "true"
  }, __ds_scope.PROJECTS.map(p => p.deps.map(d => {
    const a = center(__ds_scope.byName[d]);
    const b = center(p);
    const hot = selected === p.name || selected === d;
    return /*#__PURE__*/React.createElement("line", {
      key: `${d}->${p.name}`,
      x1: a.x,
      y1: a.y,
      x2: b.x,
      y2: b.y,
      stroke: hot ? 'var(--amber-border)' : 'var(--border-strong)',
      strokeWidth: hot ? 2 : 1
    });
  }))), __ds_scope.PROJECTS.map(p => /*#__PURE__*/React.createElement("div", {
    key: p.name,
    style: {
      position: 'absolute',
      left: p.pos.x,
      top: p.pos.y
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.DependencyGraphNode, {
    label: p.name.replace('Osys.', ''),
    status: statuses[p.name] || 'discovered',
    selected: selected === p.name,
    size: NODE,
    onClick: () => onSelect && onSelect(p.name)
  })))));
}
Object.assign(__ds_scope, { GraphView });
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/build-orchestrator/GraphView.jsx", error: String((e && e.message) || e) }); }

// ui_kits/build-orchestrator/MainWindow.jsx
try { (() => {
const BranchIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "12",
  height: "12",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "2",
  strokeLinecap: "round",
  strokeLinejoin: "round"
}, /*#__PURE__*/React.createElement("line", {
  x1: "6",
  x2: "6",
  y1: "3",
  y2: "15"
}), /*#__PURE__*/React.createElement("circle", {
  cx: "18",
  cy: "6",
  r: "3"
}), /*#__PURE__*/React.createElement("circle", {
  cx: "6",
  cy: "18",
  r: "3"
}), /*#__PURE__*/React.createElement("path", {
  d: "M18 9a9 9 0 0 1-9 9"
}));
const TreeIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "12",
  height: "12",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "2",
  strokeLinecap: "round",
  strokeLinejoin: "round"
}, /*#__PURE__*/React.createElement("path", {
  d: "M3 3v18h18"
}), /*#__PURE__*/React.createElement("path", {
  d: "M7 12h10"
}), /*#__PURE__*/React.createElement("path", {
  d: "M7 7h4"
}), /*#__PURE__*/React.createElement("path", {
  d: "M7 17h7"
}));
const PlayIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "12",
  height: "12",
  viewBox: "0 0 24 24",
  fill: "currentColor",
  stroke: "none"
}, /*#__PURE__*/React.createElement("polygon", {
  points: "6 3 20 12 6 21 6 3"
}));
const SyncIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "12",
  height: "12",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "2",
  strokeLinecap: "round",
  strokeLinejoin: "round"
}, /*#__PURE__*/React.createElement("path", {
  d: "M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"
}), /*#__PURE__*/React.createElement("path", {
  d: "M3 3v5h5"
}), /*#__PURE__*/React.createElement("path", {
  d: "M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16"
}), /*#__PURE__*/React.createElement("path", {
  d: "M21 21v-5h-5"
}));
const GearIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "14",
  height: "14",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "1.8",
  strokeLinecap: "round",
  strokeLinejoin: "round"
}, /*#__PURE__*/React.createElement("path", {
  d: "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"
}), /*#__PURE__*/React.createElement("circle", {
  cx: "12",
  cy: "12",
  r: "3"
}));
const SearchIcon = () => /*#__PURE__*/React.createElement("svg", {
  width: "12",
  height: "12",
  viewBox: "0 0 24 24",
  fill: "none",
  stroke: "currentColor",
  strokeWidth: "2",
  strokeLinecap: "round",
  strokeLinejoin: "round"
}, /*#__PURE__*/React.createElement("circle", {
  cx: "11",
  cy: "11",
  r: "8"
}), /*#__PURE__*/React.createElement("path", {
  d: "m21 21-4.3-4.3"
}));
const now = () => new Date().toTimeString().slice(0, 8);
const fmt = ms => `${(ms / 1000).toFixed(1)}s`;
function MainWindow({
  logoBase = '../../assets'
}) {
  const [statuses, setStatuses] = React.useState({});
  const [willBuild, setWillBuild] = React.useState({});
  const [durations, setDurations] = React.useState({});
  const [lines, setLines] = React.useState([{
    type: 'dim',
    time: now(),
    text: 'Build Orchestrator 2.4.1 — Osys.sln yüklendi (14 proje)'
  }, {
    type: 'info',
    time: now(),
    text: 'Proje durumları Sync sonrası görünür.'
  }]);
  const [phase, setPhase] = React.useState('idle'); // idle | synced | building | done
  const [tab, setTab] = React.useState('projects');
  const [selected, setSelected] = React.useState(null);
  const [filter, setFilter] = React.useState('');
  const [cfg, setCfg] = React.useState('Release');
  const [perf, setPerf] = React.useState('Balanced');
  const [toast, setToast] = React.useState(null);
  const [settingsOpen, setSettingsOpen] = React.useState(false);
  const [syncedAgo, setSyncedAgo] = React.useState(null);
  const timers = React.useRef([]);
  React.useEffect(() => () => timers.current.forEach(clearTimeout), []);
  const later = (fn, ms) => timers.current.push(setTimeout(fn, ms));
  const addLine = (type, text) => setLines(ls => [...ls.slice(-160), {
    type,
    time: now(),
    text
  }]);
  const errorCount = Object.values(statuses).filter(s => s === 'failed').length;
  const builtCount = Object.values(statuses).filter(s => s === 'succeeded').length;
  const skipCount = Object.values(statuses).filter(s => s === 'skipped').length;
  const toBuildNames = __ds_scope.PROJECTS.filter(p => willBuild[p.name] === 'dirty').map(p => p.name);
  const finishedOfBuild = __ds_scope.PROJECTS.filter(p => willBuild[p.name] === 'dirty' && ['succeeded', 'failed', 'skipped'].includes(statuses[p.name])).length;
  const totalMs = Object.values(durations).reduce((a, b) => a + b, 0);
  function doSync() {
    if (phase === 'building') return;
    const wb = __ds_scope.computeWillBuild();
    setWillBuild(wb);
    setStatuses({});
    setDurations({});
    setSyncedAgo('şimdi');
    const n = Object.values(wb).filter(v => v === 'dirty').length;
    addLine('cmd', 'git fetch origin && osys-state diff');
    later(() => {
      addLine('info', `Sync tamamlandı — 2 değişen proje, ${n} derlenecek`);
      addLine('dim', `${14 - n} proje güncel (atlanacak)`);
      setPhase('synced');
    }, 450);
  }
  function doBuild() {
    if (phase === 'building') return;
    const wb = Object.keys(willBuild).length ? willBuild : __ds_scope.computeWillBuild();
    if (!Object.keys(willBuild).length) setWillBuild(wb);
    const queue = new Set(__ds_scope.PROJECTS.filter(p => wb[p.name] === 'dirty').map(p => p.name));
    setPhase('building');
    setToast(null);
    setDurations({});
    const init = {};
    __ds_scope.PROJECTS.forEach(p => {
      init[p.name] = queue.has(p.name) ? 'queued' : 'skipped';
    });
    setStatuses(init);
    addLine('cmd', `msbuild Osys.sln /m /p:Configuration=${cfg} — ${queue.size} proje, ${14 - queue.size} atlandı`);
    const done = new Set(); // succeeded
    const dead = new Set(); // failed or cascade-skipped
    const running = new Set();
    const durMap = {};
    const finished = () => {
      const fails = __ds_scope.PROJECTS.filter(p => queue.has(p.name)).filter(p => p.fails).length;
      const total = Object.values(durMap).reduce((a, b) => a + b, 0);
      addLine(fails ? 'error' : 'success', fails ? `Derleme başarısız — ${fails} hata, ${done.size} başarılı (${fmt(total)})` : `Derleme tamamlandı — ${done.size} proje (${fmt(total)})`);
      setPhase('done');
      setToast(fails ? {
        tone: 'fail',
        title: 'Derleme başarısız',
        description: `${fails} hata — Osys.Parca.Api. ${done.size} proje derlendi.`
      } : {
        tone: 'success',
        title: 'Derleme tamamlandı',
        description: `${done.size} proje · ${fmt(total)}`
      });
    };
    const pump = () => {
      const ready = [...queue].filter(n => !running.has(n) && !done.has(n) && !dead.has(n) && __ds_scope.PROJECTS.find(p => p.name === n).deps.every(d => !queue.has(d) || done.has(d)));
      ready.slice(0, Math.max(0, 2 - running.size)).forEach(name => {
        const proj = __ds_scope.PROJECTS.find(p => p.name === name);
        running.add(name);
        setStatuses(s => ({
          ...s,
          [name]: 'building'
        }));
        addLine('info', `${name} derleniyor…`);
        later(() => {
          running.delete(name);
          durMap[name] = proj.dur;
          setDurations(d => ({
            ...d,
            [name]: proj.dur
          }));
          if (proj.fails) {
            dead.add(name);
            setStatuses(s => ({
              ...s,
              [name]: 'failed'
            }));
            addLine('error', `CS0246: 'OsysDbContext' türü bulunamadı — ${name} (${fmt(proj.dur)})`);
            __ds_scope.downstreamOf(name).forEach(dn => {
              if (queue.has(dn) && !done.has(dn) && !dead.has(dn)) {
                dead.add(dn);
                setStatuses(s => ({
                  ...s,
                  [dn]: 'skipped'
                }));
                addLine('warn', `${dn} atlandı (bağımlılık hatası: ${name})`);
              }
            });
          } else {
            done.add(name);
            setStatuses(s => ({
              ...s,
              [name]: 'succeeded'
            }));
            addLine('success', `${name} → bin/${cfg}/net8.0 (${fmt(proj.dur)})`);
          }
          if (done.size + dead.size >= queue.size) finished();else pump();
        }, Math.round(proj.dur * 0.55));
      });
    };
    later(pump, 350);
  }
  const visible = __ds_scope.PROJECTS.filter(p => p.name.toLowerCase().includes(filter.toLowerCase()));
  return /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      height: '100%',
      position: 'relative',
      background: 'var(--surface-base)',
      border: '1px solid var(--border)',
      borderRadius: 'var(--radius-lg)',
      overflow: 'hidden',
      fontFamily: 'var(--font-sans)',
      fontSize: 'var(--text-sm)',
      color: 'var(--text-primary)'
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.TitleBar, {
    logoSrc: `${logoBase}/delta-logo-dark.svg`,
    title: "Build Orchestrator"
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--text-2xs)',
      color: 'var(--text-faint)'
    }
  }, "Osys.sln \u2014 Release x64")), /*#__PURE__*/React.createElement(__ds_scope.Toolbar, null, /*#__PURE__*/React.createElement(__ds_scope.Chip, {
    icon: /*#__PURE__*/React.createElement(BranchIcon, null),
    label: "branch",
    value: "release/2024.2",
    chevron: true
  }), /*#__PURE__*/React.createElement(__ds_scope.Chip, {
    icon: /*#__PURE__*/React.createElement(TreeIcon, null),
    label: "worktree",
    value: "wt-ana",
    chevron: true
  }), /*#__PURE__*/React.createElement(__ds_scope.ToolbarSep, null), /*#__PURE__*/React.createElement(__ds_scope.Segment, {
    options: ['Debug', 'Release'],
    value: cfg,
    onChange: setCfg
  }), /*#__PURE__*/React.createElement(__ds_scope.Segment, {
    options: ['Full', 'Balanced', 'Light'],
    value: perf,
    onChange: setPerf
  }), /*#__PURE__*/React.createElement(__ds_scope.ToolbarSep, null), /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "secondary",
    size: "md",
    icon: /*#__PURE__*/React.createElement(SyncIcon, null),
    onClick: doSync,
    disabled: phase === 'building'
  }, "Sync"), /*#__PURE__*/React.createElement(__ds_scope.ToolbarSpacer, null), /*#__PURE__*/React.createElement("div", {
    style: {
      width: 200
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Input, {
    placeholder: "Proje filtrele\u2026",
    prefix: /*#__PURE__*/React.createElement(SearchIcon, null),
    value: filter,
    onChange: e => setFilter(e.target.value)
  })), /*#__PURE__*/React.createElement(__ds_scope.Tooltip, {
    content: "Ayarlar",
    side: "bottom"
  }, /*#__PURE__*/React.createElement(__ds_scope.IconButton, {
    title: "Ayarlar",
    onClick: () => setSettingsOpen(true)
  }, /*#__PURE__*/React.createElement(GearIcon, null))), phase === 'building' ? /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "danger",
    onClick: () => {}
  }, "Durdur") : /*#__PURE__*/React.createElement(__ds_scope.Tooltip, {
    content: "Yaln\u0131zca de\u011Fi\u015Fenleri derle",
    kbd: "F5",
    side: "bottom"
  }, /*#__PURE__*/React.createElement(__ds_scope.Button, {
    variant: "primary",
    icon: /*#__PURE__*/React.createElement(PlayIcon, null),
    onClick: doBuild
  }, "Derle"))), /*#__PURE__*/React.createElement(__ds_scope.Tabs, {
    active: tab,
    onChange: setTab,
    tabs: [{
      id: 'projects',
      label: 'Projeler',
      count: 14
    }, {
      id: 'graph',
      label: 'Bağımlılık Grafiği'
    }, {
      id: 'output',
      label: 'Çıktı',
      count: errorCount || undefined
    }]
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      alignItems: 'center',
      gap: 28,
      padding: '10px 16px',
      borderBottom: '1px solid var(--border-subtle)',
      background: 'var(--surface)'
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Metric, {
    label: "Derlenecek",
    value: phase === 'idle' ? '—' : toBuildNames.length,
    unit: phase === 'idle' ? '' : '/ 14'
  }), /*#__PURE__*/React.createElement(__ds_scope.Metric, {
    label: "Ba\u015Far\u0131l\u0131",
    value: builtCount,
    tone: builtCount ? 'success' : 'default'
  }), /*#__PURE__*/React.createElement(__ds_scope.Metric, {
    label: "Hata",
    value: errorCount,
    tone: errorCount ? 'fail' : 'default'
  }), /*#__PURE__*/React.createElement(__ds_scope.Metric, {
    label: "Atland\u0131",
    value: skipCount
  }), /*#__PURE__*/React.createElement(__ds_scope.Metric, {
    label: "Toplam s\xFCre",
    value: totalMs ? (totalMs / 1000).toFixed(1) : '—',
    unit: "s"
  }), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.ProgressBar, {
    value: toBuildNames.length ? finishedOfBuild / toBuildNames.length * 100 : 0,
    status: errorCount ? 'failed' : phase === 'done' ? 'succeeded' : 'building',
    indeterminate: phase === 'building' && finishedOfBuild === 0
  })), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--text-xs)',
      color: 'var(--text-dim)',
      fontVariantNumeric: 'tabular-nums'
    }
  }, toBuildNames.length ? `${finishedOfBuild}/${toBuildNames.length}` : '0/0')), tab === 'projects' && /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      minHeight: 0,
      display: 'flex',
      flexDirection: 'column'
    }
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 1,
      overflowY: 'auto'
    }
  }, visible.map(p => /*#__PURE__*/React.createElement(__ds_scope.ProjectRow, {
    key: p.name,
    name: p.name,
    solution: p.solution,
    status: statuses[p.name] || 'discovered',
    willBuild: willBuild[p.name] || 'unknown',
    duration: durations[p.name] ? fmt(durations[p.name]) : undefined,
    selected: selected === p.name,
    onClick: () => setSelected(p.name),
    onOpenFile: () => addLine('dim', `${p.name}.csproj Explorer'da açıldı`),
    onOpenVS: () => addLine('dim', `${p.name} Visual Studio'da açıldı`)
  })), !visible.length && /*#__PURE__*/React.createElement("div", {
    style: {
      padding: 24,
      fontSize: 'var(--text-xs)',
      color: 'var(--text-dim)'
    }
  }, "\"", filter, "\" ile e\u015Fle\u015Fen proje yok.")), /*#__PURE__*/React.createElement("div", {
    style: {
      flex: 'none',
      display: 'flex',
      alignItems: 'center',
      gap: 8,
      height: 28,
      padding: '0 12px',
      background: 'var(--console-bg)',
      borderTop: '1px solid var(--border-subtle)'
    }
  }, /*#__PURE__*/React.createElement("span", {
    style: {
      fontSize: 'var(--text-2xs)',
      fontWeight: 500,
      letterSpacing: 'var(--tracking-caps)',
      textTransform: 'uppercase',
      color: 'var(--text-faint)'
    }
  }, "\xC7\u0131kt\u0131"), /*#__PURE__*/React.createElement("span", {
    style: {
      fontFamily: 'var(--font-mono)',
      fontSize: 'var(--text-2xs)',
      color: 'var(--text-faint)'
    }
  }, lines.length, " sat\u0131r")), /*#__PURE__*/React.createElement(__ds_scope.Console, {
    height: 168,
    lines: lines,
    style: {
      flex: 'none',
      borderTop: 'none'
    }
  })), tab === 'graph' && /*#__PURE__*/React.createElement(__ds_scope.GraphView, {
    statuses: statuses,
    selected: selected,
    onSelect: setSelected
  }), tab === 'output' && /*#__PURE__*/React.createElement(__ds_scope.Console, {
    lines: lines,
    style: {
      flex: 1,
      minHeight: 0
    }
  }), /*#__PURE__*/React.createElement(__ds_scope.StatusBar, {
    right: /*#__PURE__*/React.createElement("span", {
      style: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: 10
      }
    }, /*#__PURE__*/React.createElement(__ds_scope.StatusBarItem, {
      label: "perf",
      value: perf
    }), /*#__PURE__*/React.createElement(__ds_scope.StatusBarItem, {
      label: "v",
      value: "2.4.1"
    }))
  }, /*#__PURE__*/React.createElement(__ds_scope.StatusBarItem, {
    label: "Sync",
    value: syncedAgo || 'yapılmadı'
  }), /*#__PURE__*/React.createElement(__ds_scope.StatusBarItem, {
    value: `${builtCount + skipCount + errorCount}/14`
  }), errorCount > 0 && /*#__PURE__*/React.createElement(__ds_scope.StatusBarItem, {
    label: "hata",
    value: errorCount,
    color: "var(--status-fail-text)"
  }), /*#__PURE__*/React.createElement("span", {
    style: {
      display: 'inline-flex',
      alignItems: 'center',
      gap: 4
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Kbd, null, "F5"), /*#__PURE__*/React.createElement("span", {
    style: {
      color: 'var(--text-faint)'
    }
  }, "Derle"))), toast && /*#__PURE__*/React.createElement("div", {
    style: {
      position: 'absolute',
      right: 16,
      bottom: 40,
      zIndex: 60
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Toast, {
    tone: toast.tone,
    title: toast.title,
    description: toast.description,
    onClose: () => setToast(null),
    action: toast.tone === 'fail' ? /*#__PURE__*/React.createElement(__ds_scope.Button, {
      size: "sm",
      variant: "secondary",
      onClick: () => {
        setTab('output');
        setToast(null);
      }
    }, "Konsolu A\xE7") : undefined
  })), /*#__PURE__*/React.createElement(__ds_scope.Dialog, {
    open: settingsOpen,
    title: "Derleme Ayarlar\u0131",
    onClose: () => setSettingsOpen(false),
    footer: /*#__PURE__*/React.createElement(React.Fragment, null, /*#__PURE__*/React.createElement(__ds_scope.Button, {
      variant: "ghost",
      onClick: () => setSettingsOpen(false)
    }, "Vazge\xE7"), /*#__PURE__*/React.createElement(__ds_scope.Button, {
      variant: "primary",
      onClick: () => setSettingsOpen(false)
    }, "Kaydet"))
  }, /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: 14
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Field, {
    label: "Solution yolu",
    hint: "OSYS k\xF6k\xFCnden g\xF6recel"
  }, /*#__PURE__*/React.createElement(__ds_scope.Input, {
    mono: true,
    defaultValue: "src/Osys.sln"
  })), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'grid',
      gridTemplateColumns: '1fr 1fr',
      gap: 12
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Field, {
    label: "Paralellik"
  }, /*#__PURE__*/React.createElement(__ds_scope.Input, {
    mono: true,
    defaultValue: "8"
  })), /*#__PURE__*/React.createElement(__ds_scope.Field, {
    label: "MSBuild verbosity"
  }, /*#__PURE__*/React.createElement(__ds_scope.Select, {
    defaultValue: "minimal"
  }, /*#__PURE__*/React.createElement("option", null, "quiet"), /*#__PURE__*/React.createElement("option", null, "minimal"), /*#__PURE__*/React.createElement("option", null, "normal"), /*#__PURE__*/React.createElement("option", null, "detailed")))), /*#__PURE__*/React.createElement("div", {
    style: {
      display: 'flex',
      flexDirection: 'column',
      gap: 8,
      paddingTop: 2
    }
  }, /*#__PURE__*/React.createElement(__ds_scope.Switch, {
    checked: true,
    label: "Yaln\u0131zca de\u011Fi\u015Fenleri derle",
    onChange: () => {}
  }), /*#__PURE__*/React.createElement(__ds_scope.Switch, {
    checked: true,
    label: "NuGet restore (gerekliyse)",
    onChange: () => {}
  }), /*#__PURE__*/React.createElement(__ds_scope.Switch, {
    label: "Ba\u015Far\u0131da bildirim",
    onChange: () => {}
  })))));
}
Object.assign(__ds_scope, { MainWindow });
})(); } catch (e) { __ds_ns.__errors.push({ path: "ui_kits/build-orchestrator/MainWindow.jsx", error: String((e && e.message) || e) }); }

__ds_ns.Button = __ds_scope.Button;

__ds_ns.Chip = __ds_scope.Chip;

__ds_ns.IconButton = __ds_scope.IconButton;

__ds_ns.Kbd = __ds_scope.Kbd;

__ds_ns.Segment = __ds_scope.Segment;

__ds_ns.ConsoleLine = __ds_scope.ConsoleLine;

__ds_ns.Console = __ds_scope.Console;

__ds_ns.Metric = __ds_scope.Metric;

__ds_ns.ProgressBar = __ds_scope.ProgressBar;

__ds_ns.ProjectRow = __ds_scope.ProjectRow;

__ds_ns.Tag = __ds_scope.Tag;

__ds_ns.Checkbox = __ds_scope.Checkbox;

__ds_ns.Field = __ds_scope.Field;

__ds_ns.Input = __ds_scope.Input;

__ds_ns.Select = __ds_scope.Select;

__ds_ns.Switch = __ds_scope.Switch;

__ds_ns.DependencyGraphNode = __ds_scope.DependencyGraphNode;

__ds_ns.Dialog = __ds_scope.Dialog;

__ds_ns.StatusBar = __ds_scope.StatusBar;

__ds_ns.StatusBarItem = __ds_scope.StatusBarItem;

__ds_ns.Tabs = __ds_scope.Tabs;

__ds_ns.TitleBar = __ds_scope.TitleBar;

__ds_ns.Toast = __ds_scope.Toast;

__ds_ns.Toolbar = __ds_scope.Toolbar;

__ds_ns.ToolbarSep = __ds_scope.ToolbarSep;

__ds_ns.ToolbarSpacer = __ds_scope.ToolbarSpacer;

__ds_ns.Tooltip = __ds_scope.Tooltip;

__ds_ns.Spinner = __ds_scope.Spinner;

__ds_ns.StatusBadge = __ds_scope.StatusBadge;

__ds_ns.STATUS_META = __ds_scope.STATUS_META;

__ds_ns.StatusGlyph = __ds_scope.StatusGlyph;

__ds_ns.WillBuildDot = __ds_scope.WillBuildDot;

__ds_ns.PROJECTS = __ds_scope.PROJECTS;

__ds_ns.DIRTY_ROOTS = __ds_scope.DIRTY_ROOTS;

__ds_ns.GraphView = __ds_scope.GraphView;

__ds_ns.MainWindow = __ds_scope.MainWindow;

})();
