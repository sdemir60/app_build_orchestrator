// Delta Build Orchestrator — component props (documentation; not type-checked).
// Runtime: window.DeltaBuildOrchestratorDS_eb0bd1 — also exports STATUS_META.
import * as React from 'react';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> { variant?: 'primary'|'secondary'|'ghost'|'danger'; size?: 'sm'|'md'|'lg'; icon?: React.ReactNode; disabled?: boolean; style?: React.CSSProperties; }
export interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> { size?: 'sm'|'md'|'lg'; active?: boolean; disabled?: boolean; title?: string; children?: React.ReactNode; style?: React.CSSProperties; }
export interface ChipProps { icon?: React.ReactNode; label?: React.ReactNode; value?: React.ReactNode; chevron?: boolean; active?: boolean; onRemove?: () => void; disabled?: boolean; onClick?: () => void; style?: React.CSSProperties; }
export interface SegmentProps { options: Array<string | { value: string; label: React.ReactNode }>; value?: string; onChange?: (value: string) => void; size?: 'sm'|'md'; style?: React.CSSProperties; }
export interface KbdProps { children?: React.ReactNode; style?: React.CSSProperties; }
export interface FieldProps { label?: React.ReactNode; hint?: React.ReactNode; error?: React.ReactNode; htmlFor?: string; inline?: boolean; children?: React.ReactNode; style?: React.CSSProperties; }
export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> { mono?: boolean; invalid?: boolean; prefix?: React.ReactNode; style?: React.CSSProperties; }
export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> { invalid?: boolean; children?: React.ReactNode; style?: React.CSSProperties; }
export interface CheckboxProps { checked?: boolean; indeterminate?: boolean; onChange?: (checked: boolean) => void; label?: React.ReactNode; disabled?: boolean; style?: React.CSSProperties; }
export interface SwitchProps { checked?: boolean; onChange?: (checked: boolean) => void; label?: React.ReactNode; disabled?: boolean; style?: React.CSSProperties; }
export type Status = 'discovered'|'queued'|'building'|'succeeded'|'failed'|'skipped'|'cycle';
export interface StatusGlyphProps { status?: Status; size?: number; title?: string; style?: React.CSSProperties; }
export interface StatusBadgeProps { status?: Status; label?: React.ReactNode; size?: 'sm'|'md'; detail?: React.ReactNode; style?: React.CSSProperties; }
export interface SpinnerProps { size?: number; color?: string; title?: string; style?: React.CSSProperties; }
export interface ProjectRowProps { name: string; solution?: string; status?: Status; duration?: React.ReactNode; selected?: boolean; compact?: boolean; onOpenFile?: () => void; onOpenVS?: () => void; onClick?: () => void; style?: React.CSSProperties; }
export interface ConsoleLineData { type?: 'info'|'success'|'warn'|'error'|'cmd'|'dim'; time?: string; text: React.ReactNode; }
export interface ConsoleProps { lines?: ConsoleLineData[]; height?: number|string; autoScroll?: boolean; children?: React.ReactNode; style?: React.CSSProperties; }
export interface ConsoleLineProps { type?: ConsoleLineData['type']; time?: string; children?: React.ReactNode; style?: React.CSSProperties; }
export interface ProgressBarProps { value?: number; status?: 'building'|'succeeded'|'failed'|'neutral'; indeterminate?: boolean; height?: number; style?: React.CSSProperties; }
export interface MetricProps { label: React.ReactNode; value: React.ReactNode; unit?: React.ReactNode; tone?: 'default'|'success'|'fail'|'amber'; size?: 'md'|'lg'; style?: React.CSSProperties; }
export interface TagProps { children?: React.ReactNode; mono?: boolean; tone?: 'neutral'|'dim'|'amber'; style?: React.CSSProperties; }
export interface DependencyGraphNodeProps { label?: string; status?: Status; selected?: boolean; size?: number; showLabel?: boolean; onClick?: () => void; style?: React.CSSProperties; }
export interface TitleBarProps { title?: string; logoSrc?: string; children?: React.ReactNode; onMinimize?: () => void; onMaximize?: () => void; onClose?: () => void; style?: React.CSSProperties; }
export interface ToolbarProps { children?: React.ReactNode; style?: React.CSSProperties; }
export interface ToolbarSepProps { style?: React.CSSProperties; }
export interface TabsProps { tabs: Array<{ id: string; label: React.ReactNode; count?: number }>; active?: string; onChange?: (id: string) => void; style?: React.CSSProperties; }
export interface StatusBarProps { children?: React.ReactNode; right?: React.ReactNode; style?: React.CSSProperties; }
export interface StatusBarItemProps { label?: React.ReactNode; value?: React.ReactNode; color?: string; style?: React.CSSProperties; }
export interface TooltipProps { content: React.ReactNode; kbd?: React.ReactNode; side?: 'top'|'bottom'|'right'|'left'; children?: React.ReactNode; style?: React.CSSProperties; }
export interface DialogProps { open?: boolean; title?: React.ReactNode; width?: number; onClose?: () => void; footer?: React.ReactNode; children?: React.ReactNode; style?: React.CSSProperties; }
export interface StatusMetaEntry { color: string; label: string; }
export declare const STATUS_META: Record<Status, StatusMetaEntry>;
