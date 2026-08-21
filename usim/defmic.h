#pragma once

#include "usim.h"

#if LISPM_SYSTEM == 7800L
#include "defmic78.h"
#elif LISPM_SYSTEM == 9800L
#include "defmic98.h"
#elif LISPM_SYSTEM == 9900L
#include "defmic99.h"
#elif LISPM_SYSTEM == 9999L
#include "defmic300.h"
#endif
