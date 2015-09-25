'''

	Harness Toolset

	Copyright (c) 2015 Rich Kelley

	Contact: 
	    @RGKelley5
	    RK5DEVMAIL[A T]gmail[D O T]com
	    www.frogstarworldc.com

	License: MIT


'''
from threading import Lock

sessions = {}
session_completions = []
session_lock = Lock()