#pragma once

#include	"usim.h"

#if LISPM_SYSTEM == 7800L
#include "qcom78.h"
#elif LISPM_SYSTEM == 9800L
#include "qcom98.h"
#elif LISPM_SYSTEM == 9900L
#include "qcom99.h"
#elif LISPM_SYSTEM == 9999L
#include "qcom300.h"
#endif

#define DATA_TYPE(x) ldb(Q_DATA_TYPE_, (x))
