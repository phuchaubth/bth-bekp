/** Wallboard rotation: screen 0 is the hospital overview, screens 1..n are departments. */
export interface RotationState {
  screen: number;
  startedAt: number;
}

export function nextRotation(state: RotationState, now: number, periodMs: number, screenCount: number): RotationState {
  if (screenCount <= 0) {
    return { screen: 0, startedAt: now };
  }

  const elapsed = now - state.startedAt;
  if (elapsed < periodMs) {
    return state.screen < screenCount ? state : { screen: 0, startedAt: now };
  }

  const steps = Math.floor(elapsed / periodMs);
  return { screen: (state.screen + steps) % screenCount, startedAt: state.startedAt + steps * periodMs };
}

export function clampSeconds(raw: string | null, fallback = 15): number {
  const value = Number.parseInt(raw ?? '', 10);
  if (Number.isNaN(value)) {
    return fallback;
  }

  return Math.min(120, Math.max(5, value));
}
