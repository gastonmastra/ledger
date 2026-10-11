import { DeviceSession } from './device-session';

/** An in-memory Storage. */
function memory(): Storage {
  const data = new Map<string, string>();
  return {
    get length() { return data.size; },
    clear: () => data.clear(),
    getItem: (k) => data.get(k) ?? null,
    key: (i) => [...data.keys()][i] ?? null,
    removeItem: (k) => void data.delete(k),
    setItem: (k, v) => void data.set(k, String(v)),
  };
}

describe('DeviceSession (remember me on this device)', () => {
  it('defaults to remembering only in the installed app', () => {
    expect(new DeviceSession(memory(), memory(), () => true).remember).toBe(true);
    expect(new DeviceSession(memory(), memory(), () => false).remember).toBe(false);
  });

  it('keeps the tokens in localStorage when remembering, in sessionStorage otherwise', () => {
    const local = memory();
    const session = memory();
    const device = new DeviceSession(local, session, () => false);

    expect(device.storage()).toBe(session);
    device.setRemember(true);
    expect(device.storage()).toBe(local);
    expect(new DeviceSession(local, session, () => false).remember).toBe(true); // the choice is kept
  });

  it('moves the current login to the new place and clears it on logout', () => {
    const local = memory();
    const session = memory();
    const device = new DeviceSession(local, session, () => false);
    session.setItem('access_token', 'abc');
    session.setItem('refresh_token', 'def');
    session.setItem('ledger.other', 'kept');

    device.setRemember(true);
    expect(local.getItem('access_token')).toBe('abc');
    expect(local.getItem('refresh_token')).toBe('def');
    expect(session.getItem('access_token')).toBeNull();
    expect(session.getItem('ledger.other')).toBe('kept'); // only the login moves

    device.setRemember(false);
    expect(session.getItem('access_token')).toBe('abc');
    expect(local.getItem('access_token')).toBeNull();

    device.clear();
    expect(session.getItem('access_token')).toBeNull();
    expect(session.getItem('refresh_token')).toBeNull();
  });
});
