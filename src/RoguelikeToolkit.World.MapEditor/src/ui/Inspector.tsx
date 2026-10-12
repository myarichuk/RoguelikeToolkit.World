import { useApp, useViewModel } from './hooks';
import { Section, Select, Slider, TextField } from './controls';
import { FACT_HINTS } from './glossary';

const KIND_TITLE: Record<string, string> = {
  capital: 'Capital', city: 'City', town: 'Town', village: 'Village', river: 'River', lake: 'Lake', sea: 'Sea', ocean: 'Ocean',
  mountain: 'Mountain range', valley: 'Valley', forest: 'Forest', deposit: 'Resource deposit',
};

export function Inspector() {
  const { vms } = useApp();
  const vm = vms.inspector;
  const s = useViewModel(vm);

  if (s.mode === 'feature' && s.feature) {
    const f = s.feature;
    return (
      <Section title={KIND_TITLE[f.kind] ?? f.kind} actions={<button onClick={vm.clear} title="Clear selection (Esc)" aria-label="Clear selection">×</button>}>
        {f.kind !== 'deposit' && (
          <div className="name-row">
            <TextField value={f.name} onCommit={vm.rename} placeholder="Name" />
            {f.canReroll && <button onClick={vm.reroll} title="Generate another name in this culture" aria-label="Generate another name">⟳</button>}
          </div>
        )}
        {f.culture && f.kind !== 'deposit' && <p className="muted small">Named in the {f.culture} tradition</p>}
        <dl className="details">
          {f.details.map(([k, v]) => <div key={k}><dt title={FACT_HINTS[k]}>{k}</dt><dd>{v}</dd></div>)}
        </dl>
        <div className="buttons">
          <button onClick={vm.focus}>Zoom to</button>
          {f.hasLabel && <button onClick={vm.editLabel}>Edit label</button>}
          <button className="danger" onClick={vm.deleteFeature} title="Removes the feature, its label, river and marker (undoable)">Delete</button>
        </div>
      </Section>
    );
  }

  if (s.mode === 'labels' && s.label) {
    const l = s.label;
    return (
      <Section title={s.labelCount > 1 ? `${s.labelCount} labels` : 'Label'} actions={<button onClick={vm.clear} aria-label="Clear selection">×</button>}>
        {s.labelCount === 1 && <TextField label="Text" value={l.text} onCommit={(text) => vm.updateLabel({ text })} />}
        <Select label="Style" value={l.style} options={s.labelStyles.map((k) => ({ value: k, label: k }))} onChange={(style) => vm.updateLabel({ style, size: 0 })} />
        <Slider label="Size" value={l.size} min={0} max={60} step={0.5} onChange={(size) => vm.updateLabel({ size }, 'size')} format={(v) => (v ? v.toFixed(1) : 'style')} />
        <Slider label="Tracking" value={Number.isNaN(l.spacing) ? 0 : l.spacing} min={-0.1} max={1.2} step={0.01} onChange={(spacing) => vm.updateLabel({ spacing }, 'spacing')} />
        {!l.path && <Slider label="Angle" value={(l.angle * 180) / Math.PI} min={-90} max={90} step={1} onChange={(a) => vm.updateLabel({ angle: (a * Math.PI) / 180 }, 'angle')} format={(v) => `${v.toFixed(0)}°`} />}
        {!l.path && <Slider label="Curve" value={l.curve} min={-1} max={1} step={0.01} onChange={(curve) => vm.updateLabel({ curve }, 'curve')} />}
        {l.path && <p className="muted small">Follows a river; drag to move it along with its path.</p>}
        <div className="buttons"><button className="danger" onClick={vm.deleteSelected}>Delete</button></div>
      </Section>
    );
  }

  if (s.mode === 'stamps' && s.stamp) {
    const st = s.stamp;
    return (
      <Section title={s.stampCount > 1 ? `${s.stampCount} stamps` : s.stampName} actions={<button onClick={vm.clear} aria-label="Clear selection">×</button>}>
        <Slider label="Scale" value={st.scale} min={0.2} max={4} step={0.01} onChange={(scale) => vm.updateStamp({ scale }, 'scale')} />
        <Slider label="Rotation" value={(st.rotation * 180) / Math.PI} min={-180} max={180} step={1} onChange={(a) => vm.updateStamp({ rotation: (a * Math.PI) / 180 }, 'rotation')} format={(v) => `${v.toFixed(0)}°`} />
        <div className="buttons">
          <button onClick={() => vm.updateStamp({ flip: !st.flip })}>Flip</button>
          <button onClick={() => vm.updateStamp({ variant: st.variant + 1 })}>Next variant</button>
          <button className="danger" onClick={vm.deleteSelected}>Delete</button>
        </div>
      </Section>
    );
  }

  return null;
}
