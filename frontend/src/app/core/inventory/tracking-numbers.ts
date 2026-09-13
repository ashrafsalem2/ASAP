/**
 * Reads serial or lot numbers out of what somebody typed.
 *
 * Commas, semicolons, spaces and new lines all separate, because a scanner sends one number per
 * line and a person typing sends them with commas, and the screen should not care which. Upper
 * case, because the server stores them that way and compares them that way.
 */
export function parseTrackingNumbers(text: string | null | undefined): string[] {
  return (text ?? '')
    .split(/[\s,;]+/)
    .map((number) => number.trim().toUpperCase())
    .filter((number) => number.length > 0);
}

/**
 * The quantity a line moves when numbers were keyed and a quantity was not.
 *
 * Several serials are that many units. One number is either a lot or a single serial, and the
 * screen cannot tell which, so it takes what is outstanding: right for a lot, and for a serial the
 * server says plainly that a serial is one unit.
 */
export function quantityForNumbers(
  keyed: number | null,
  numbers: string[],
  outstanding: number,
): number {
  if ((keyed ?? 0) > 0) {
    return keyed as number;
  }

  return numbers.length > 1 ? numbers.length : outstanding;
}
