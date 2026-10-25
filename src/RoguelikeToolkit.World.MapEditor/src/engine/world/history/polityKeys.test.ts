import { describe, expect, it } from 'vitest';
import { parsePolityKey } from './polityKeys';

describe('parsePolityKey', () => {
  it('reads all seven fields, architecture last', () => {
    expect(parsePolityKey('dwarf\tdwarven_clan\t\tDwarven clan\tdwarf\tfantasy_common\tdwarven')).toEqual({
      folk: 'dwarf', culture: 'dwarven_clan', overlay: '', displayName: 'Dwarven clan', styleRace: 'dwarf', styleCulture: 'fantasy_common', architecture: 'dwarven',
    });
  });

  it('tolerates an older engine\'s six-field row: no architecture means the default look', () => {
    const k = parsePolityKey('orc\torcish_horde\twar\tOrcish horde\torc\tfantasy_common');
    expect(k.styleCulture).toBe('fantasy_common');
    expect(k.architecture).toBe('');
  });

  it('tolerates short and missing rows', () => {
    expect(parsePolityKey('human')).toMatchObject({ folk: 'human', culture: '', architecture: '' });
    expect(parsePolityKey(undefined).folk).toBe('');
    expect(parsePolityKey('a\tb\tc\td\te\tf\t ').architecture).toBe('');
  });
});
