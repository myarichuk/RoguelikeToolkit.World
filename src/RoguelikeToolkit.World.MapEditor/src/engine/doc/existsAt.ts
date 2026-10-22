import { existsAt } from '../world/history/timeline';
import type { MapDocument } from './MapDocument';
import { featureIdOfLabel, featureIdOfStamp } from './types';

/** Whether a feature's own label exists at a history turn (-1 = the end). Labels the user placed by hand always do. */
export const labelExistsAt = (doc: MapDocument, labelId: string, turn: number): boolean => ownerExistsAt(doc, featureIdOfLabel(labelId), turn);

/** Whether a settlement's stamp exists at a history turn. Stamps of decoration and relief always do. */
export const stampExistsAt = (doc: MapDocument, stampId: string, turn: number): boolean => ownerExistsAt(doc, featureIdOfStamp(stampId), turn);

const ownerExistsAt = (doc: MapDocument, featureId: string | undefined, turn: number): boolean =>
  featureId === undefined || existsAt(doc.features.get(featureId)?.span, turn);
