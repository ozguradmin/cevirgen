// SPDX-License-Identifier: AGPL-3.0-only
package com.cevirgen.tv;

import static org.junit.Assert.assertFalse;

import org.junit.Test;

/** Google Play paketinde katman modu kapalıdır (TV-TR kuralı). */
public class PlayFlavorTest {
    @Test
    public void overlayModeIsOff() {
        assertFalse(BuildConfig.OVERLAY_MODE);
    }
}
